plugins {
    id("java")
    // Matched to the platform's own: Rider 2025.2 is built with Kotlin 2.2,
    // and compiling against it with an older compiler fails on every module
    // it ships — "binary version of its metadata is 2.2.0, expected 2.0.0".
    id("org.jetbrains.kotlin.jvm") version "2.2.0"
    id("org.jetbrains.intellij.platform") version "2.18.1"
}

group = "com.basalt"
version = "1.0.0"

repositories {
    mavenCentral()

    intellijPlatform {
        defaultRepositories()
    }
}

dependencies {
    intellijPlatform {
        // Rider, because the language server is for .NET projects. The LSP
        // client API is part of the platform from 2023.2 and is available in
        // paid IDEs, which Rider is.
        //
        // 2025.2 rather than an older one: the instrumentation the Gradle
        // plugin runs needs a compiler artifact that JetBrains no longer
        // publishes for 2024.3, so building against it fails on a download
        // that will never succeed.
        rider("2025.2")

        // For the TextMate grammar: the bundled plugin supplies the API the
        // bundle provider implements. Declared as bundled rather than as a
        // dependency to download — it ships inside the IDE.
        bundledPlugin("org.jetbrains.plugins.textmate")
    }
}

intellijPlatform {
    pluginConfiguration {
        ideaVersion {
            sinceBuild = "252"
        }
    }
}

kotlin {
    jvmToolchain(21)
}

// The IntelliJ Platform ships its own Kotlin standard library, and the Gradle
// Kotlin plugin adds another by default. Two of them in one classpath is a
// version conflict the platform warns about at build time and that shows up
// as a missing method at run time.
// https://jb.gg/intellij-platform-kotlin-stdlib
dependencies {
    compileOnly(kotlin("stdlib"))
}

// The language server travels beside the jar, not inside it.
//
// Anything under src/main/resources is packed into the jar, and a file inside
// an archive cannot be executed: the plugin looked for lib/server/… , found
// nothing, and started no server at all — installed cleanly, coloured nothing,
// completed nothing.
//
// Kept out of the resources folder for the same reason, so a stale copy
// cannot end up in both places.
val serverFolder = layout.projectDirectory.dir("server")

// The grammar travels beside the jar too, for the same reason: the provider
// resolves it from the plugin's own path, and anything under resources is
// packed inside where no file path reaches it.
val grammarFolder = layout.projectDirectory.dir("textmate")

tasks.named<org.gradle.jvm.tasks.Jar>("jar") {
    // In case an older layout left one behind.
    exclude("server/**")
}

tasks.named<org.jetbrains.intellij.platform.gradle.tasks.PrepareSandboxTask>("prepareSandbox") {
    // The plugin folder is named after the project, and the task's own
    // pluginDirectory cannot be read while the task is still running.
    from(grammarFolder) {
        into(project.name + "/lib/textmate")
    }

    from(serverFolder) {
        into(project.name + "/lib/server")

        // Zip does not carry the executable bit on every platform, and a
        // server the plugin can find but not run fails the same way as one
        // that is missing — silently.
        filePermissions { unix("rwxr-xr-x") }
    }
}
