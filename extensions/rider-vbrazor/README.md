# Basalt VB.NET Razor — Rider plugin

Razor views written in Visual Basic, in JetBrains Rider.

ASP.NET Core's Razor compiler emits C# and only C#, so a `.vbhtml` file — a
real file type in ASP.NET before Core — has no compiler and no editor support
anywhere. This plugin is the editing half; the `Basalt.Razor.Vb` NuGet package
is the compiler.

## What it does

| | |
|---|---|
| Completion | including the model's own members: type `@Model.` and the properties of the class are offered |
| Diagnostics | as you type, against the view and the line you wrote |
| Hover | signature and documentation of what is under the pointer |
| Go to definition | from a name in the markup through to the `.vb` that declares it |
| Signature help | inside a call, with the current parameter marked |
| Folding, outline | from the view's own structure |

It needs a solution or a `.vbproj` in the folder you open: the language server
compiles the project to answer about types, and finds one on its own. A single
loose file gets markup support and nothing more — which it says, rather than
guessing.

## Installing it

Build it (below), then in Rider: **Settings → Plugins → ⚙ → Install Plugin
from Disk**, and pick the zip from `build/distributions`. Rider restarts and
`.vbhtml` files open as Razor.

## Compiling the views

Editing is not building. In the project holding the views:

```xml
<PackageReference Include="Basalt.Razor.Vb" Version="1.0.0" />
```

Every `.vbhtml` is then compiled by a source generator into Visual Basic,
alongside the rest of the code, and an error in a view is reported at its own
line.

## What you need first

**A JDK, version 21 or later** — and if Rider is installed you already have
one, because it ships with its own:

    # macOS
    export JAVA_HOME=/Applications/Rider.app/Contents/jbr/Contents/Home

    # Windows, from the Toolbox or a standalone install
    set JAVA_HOME=C:\Program Files\JetBrains\JetBrains Rider <version>\jbr

    # Linux
    export JAVA_HOME=~/.local/share/JetBrains/Toolbox/apps/rider/jbr

Check it with `"$JAVA_HOME/bin/java" -version`; anything 21 or above will do.
Failing that, install one:

    brew install openjdk@21                         # macOS
    sudo apt install openjdk-21-jdk                 # Debian, Ubuntu
    winget install EclipseAdoptium.Temurin.21.JDK   # Windows

**Gradle** comes with the folder: `gradlew` downloads the right version on
first use, so nothing to install.

## Building it

    export JAVA_HOME=/Applications/Rider.app/Contents/jbr/Contents/Home
    ./gradlew buildPlugin

The first build downloads Rider 2025.2 to compile against — about 1.5 GB, and
several minutes on a cold cache. It is kept afterwards.

The result is a zip under `build/distributions`.

The language server must be published into `src/main/resources/server/` first:

    dotnet publish ../../src/Basalt.Razor.Vb.LanguageServer \
        -c Release -r <runtime-identifier> --self-contained \
        -o src/main/resources/server

The runtime identifier is the machine the plugin will run on: `osx-arm64`,
`osx-x64`, `linux-x64` or `win-x64`.

The result is a zip under `build/distributions`, installable through
**Settings → Plugins → Install Plugin from Disk**.

## When it does not build

**"No matching toolchains found for Java 21"** — Gradle found a JDK, but an
older one. Point it at Rider's:

    ./gradlew buildPlugin \
        -Dorg.gradle.java.home=/Applications/Rider.app/Contents/jbr/Contents/Home

**"Cannot resolve rider:&lt;version&gt;"** — the IntelliJ repositories were not
reachable. It is a plain download; a proxy or an offline machine is the usual
reason.

**"Could not find java-compiler-ant-tasks"** — JetBrains stops publishing that
artifact for older platform releases, so building against one that used to work
fails on a download that will never succeed. Move `rider(...)` in
`build.gradle.kts` to a current version.

**"Module was compiled with an incompatible version of Kotlin"** — the Kotlin
plugin version has to match the platform's own. Rider 2025.2 is built with
Kotlin 2.2; an older compiler fails on every module it ships.

**"IntelliJ Platform Gradle Plugin requires Gradle 9.0.0 and higher"** — update
the wrapper:

    ./gradlew wrapper --gradle-version 9.0

**Installed, but nothing is coloured.** The colouring comes from the server as
semantic tokens, and the platform asks for them only if the descriptor says so
— `lspSemanticTokensSupport`. Reinstall the plugin after a rebuild: an old zip
still on disk installs cleanly and does nothing.

**Still nothing, and no completion either.** The server did not start.

Read the log, which is a plain file — no menu command needed:

    # macOS
    grep -iE "vbrazor|SEVERE" ~/Library/Logs/JetBrains/Rider*/idea.log | tail -20

    # Windows
    findstr /i vbrazor %LOCALAPPDATA%\JetBrains\Rider*\log\idea.log

    # Linux
    grep -i vbrazor ~/.cache/JetBrains/Rider*/log/idea.log | tail -20

What the failures look like there:

| In the log | What happened |
|---|---|
| `getLocation() is null` | the plugin asked the class loader where its jar is, which Rider does not answer — fixed by looking itself up through `PluginManagerCore` |
| nothing at all about the plugin | it did not load: check it is enabled in **Settings → Plugins** |
| the server path, then silence | the server was found and would not run — usually the executable bit, which a zip does not carry |
| `Basalt: no project loaded — …` | the server started but found no solution: completion offers markup and directives, and knows no types |
| `Basalt: the project is loaded` | everything is in place; if completion still knows nothing, the plugin is an old build |

**Completion offers keywords but no members.** Either the server found no
project — the log says which, see above — or the installed plugin predates the
build. Compare the dates:

    ls -l ~/Library/Application\ Support/JetBrains/Rider*/plugins/rider-vbrazor/lib/server/vbrazor-langserver
    ls -l build/distributions/rider-vbrazor-1.0.0.zip

Installing does not overwrite in place: an older plugin keeps answering until
it is replaced and Rider restarted.

## Note on the LSP client

Rider's LSP client API is available in the paid JetBrains IDEs; it is not in
the community editions. Rider is a paid IDE, so this plugin works there — but
the same code would not run in IntelliJ IDEA Community.
