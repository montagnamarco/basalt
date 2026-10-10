using System.Collections;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.Loader;

namespace Basalt.Shell.Controls;

/// <summary>
/// Drops what the framework's TypeDescriptor remembers about the types of a design context
/// that is being unloaded.
/// </summary>
/// <remarks>
/// TypeDescriptor caches property descriptors and provider lookups per Type in static tables
/// that nothing public ever empties: neither <see cref="TypeDescriptor.Refresh(Assembly)"/> nor
/// the hot-reload cache handler removes them. Measured on .NET 10 by scanning static fields
/// after <c>TypeDescriptor.GetProperties</c> on a project control: the context stayed alive
/// through <c>ReflectTypeDescriptionProvider.s_propertyCache</c> and
/// <c>TypeDescriptor.s_defaultProviderInitialized</c>, so every rebuild leaked the previous
/// build of the user's assembly.
///
/// The tables are private, so they are found by reflection: every static dictionary of
/// TypeDescriptor and ReflectTypeDescriptionProvider loses the keys that belong to the
/// context. If a later runtime renames them this does nothing, and the designer's
/// collection test fails rather than the leak going unnoticed.
/// </remarks>
internal static class WinFormsDesignTimeCaches
{
    private const BindingFlags StaticFields = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    /// <summary>
    /// Removes the context's keys from every table this class finds. It never throws: it runs inside
    /// <see cref="AssemblyLoadContext"/>.Unloading, and an exception there escapes Unload and stops it, which is
    /// worse than the leak. Each table and each provider is handled on its own, so a failure skips only that one.
    /// </summary>
    public static void Release(AssemblyLoadContext context)
    {
        var owners = new[]
        {
            typeof(TypeDescriptor),
            typeof(TypeDescriptor).Assembly.GetType("System.ComponentModel.ReflectTypeDescriptionProvider")
        };

        foreach (var owner in owners)
        {
            if (owner is null) continue;

            foreach (var field in FieldsOf(owner, StaticFields))
            {
                Attempt(() =>
                {
                    if (field.GetValue(null) is IDictionary table) RemoveKeysOf(context, table);
                });
            }
        }

        // The shared reflection provider also keeps a per-type table of its own
        // (ReflectTypeDescriptionProvider._typeData, measured as the last root once the
        // static tables were clean). Providers are reached through the node chain that
        // TypeDescriptor hands out for object, which every type falls back to.
        foreach (var provider in ProvidersOf())
        {
            Attempt(() => ReleaseProvider(context, provider));
        }
    }

    /// <summary>
    /// Every provider TypeDescriptor has handed out: the chain for object, plus the
    /// chains stored in its private s_providerTable. A constructed type of the context
    /// (List(Of T) of a project type, asked for its converter by the Properties window)
    /// got a node there whose provider was not the one reached from object; measured as
    /// the last root by walking TypeDescriptor's statics.
    /// </summary>
    private static IEnumerable<object> Providers()
    {
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var nodes = new List<object?> { TypeDescriptor.GetProvider(typeof(object)) };

        if (typeof(TypeDescriptor).GetField("s_providerTable", StaticFields)?.GetValue(null) is IDictionary table)
        {
            // A snapshot: the table is a WeakHashtable the framework keeps changing, and
            // walking it live is what hung an earlier attempt.
            lock (table) nodes.AddRange(table.Values.Cast<object?>().ToList());
        }

        foreach (var start in nodes)
        {
            foreach (var provider in ProvidersFrom(start, seen)) yield return provider;
        }
    }

    /// <summary>The providers, or none when they cannot be listed: a failure here must not stop the unload.</summary>
    private static List<object> ProvidersOf()
    {
        try
        {
            return Providers().ToList();
        }
        catch (Exception)
        {
            // Dropped on purpose (see Release): the leak is then left as it was, and the collection test reports it.
            return [];
        }
    }

    private static IEnumerable<object> ProvidersFrom(object? start, HashSet<object> seen)
    {
        // A TypeDescriptionNode is a linked list whose Provider field is the real provider
        // and whose Next field is the one behind it. The seen set stops at a node or
        // provider already visited, so shared tails and any cycle are walked once.
        for (var node = start; node is not null && seen.Add(node);
             node = node.GetType().GetField("Next", InstanceFields)?.GetValue(node))
        {
            var provider = node.GetType().GetField("Provider", InstanceFields)?.GetValue(node) ?? node;
            if (ReferenceEquals(provider, node) || seen.Add(provider)) yield return provider;
        }
    }

    private static void ReleaseProvider(AssemblyLoadContext context, object provider)
    {
        // A provider whose own type belongs to the context goes away with that context, so its tables are not read.
        if (TypeBelongsTo(context, provider.GetType())) return;

        foreach (var field in FieldsOf(provider.GetType(), InstanceFields))
        {
            Attempt(() =>
            {
                if (field.GetValue(provider) is IDictionary table) RemoveKeysOf(context, table);
            });
        }
    }

    /// <summary>The fields of a type, or none when they cannot be read: a failure skips that type only.</summary>
    private static FieldInfo[] FieldsOf(Type type, BindingFlags flags)
    {
        try
        {
            return type.GetFields(flags);
        }
        catch (Exception)
        {
            // Dropped on purpose (see Release): this type's tables are skipped.
            return [];
        }
    }

    /// <summary>Runs one step of the release. Any exception is dropped, for the reason given on <see cref="Release"/>.</summary>
    private static void Attempt(Action step)
    {
        try
        {
            step();
        }
        catch (Exception)
        {
            // Dropped on purpose (see Release): the table this step was clearing keeps its keys, and the collection test reports it.
        }
    }

    private static void RemoveKeysOf(AssemblyLoadContext context, IDictionary table)
    {
        // The lock is the table object itself, which is the lock the framework's TypeDescriptor takes on its tables when
        // it writes them. Hashtable.SyncRoot is another object, and it is not what the framework locks. A
        // ConcurrentDictionary has no SyncRoot at all, but a lock on it still serialises the removal.
        lock (table) RemoveStale(context, table);
    }

    private static void RemoveStale(AssemblyLoadContext context, IDictionary table)
    {
        var stale = table.Keys.Cast<object>().Where(key => BelongsTo(context, key)).ToList();
        foreach (var key in stale) table.Remove(key);
    }

    private static bool BelongsTo(AssemblyLoadContext context, object key)
    {
        var type = key as Type ?? (key as MemberInfo)?.DeclaringType;
        return type is not null && TypeBelongsTo(context, type);
    }

    /// <summary>
    /// Whether the type is in the context, or is built from a type that is: an array of a user type or a
    /// constructed generic such as <c>List(Of UserType)</c> lives in the framework's assembly, but its entry
    /// still holds the user's type, so it is dropped with the context all the same.
    /// </summary>
    private static bool TypeBelongsTo(AssemblyLoadContext context, Type type)
    {
        if (type.HasElementType && type.GetElementType() is { } element && TypeBelongsTo(context, element)) return true;
        if (type.IsGenericType && type.GetGenericArguments().Any(argument => TypeBelongsTo(context, argument))) return true;

        return ReferenceEquals(AssemblyLoadContext.GetLoadContext(type.Assembly), context);
    }
}
