namespace System.Runtime.CompilerServices;

/// <summary>
/// Marker the compiler needs for init-only properties and records.
///
/// It ships with .NET 5 and later but not with netstandard2.0, which is the
/// framework a source generator must target. Declaring it here is the
/// documented way to use the syntax on older targets; the runtime never looks
/// at it.
/// </summary>
internal static class IsExternalInit;
