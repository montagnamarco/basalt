package com.basalt.vbrazor

import com.intellij.ide.plugins.PluginManagerCore
import com.intellij.openapi.extensions.PluginId
import org.jetbrains.plugins.textmate.api.TextMateBundleProvider

/**
 * The HTML half of a view's colouring, from a TextMate grammar.
 *
 * The language server colours the Visual Basic, because it is the parser that
 * compiles the view and knows exactly where the code begins and ends. It says
 * nothing about the markup on purpose: a grammar already colours HTML, and a
 * second opinion on the same question is free to disagree with the first.
 *
 * Rider ships no grammar for .vbhtml, which is why the markup showed black
 * while the code around it was coloured. This is the same grammar the VS Code
 * extension uses, so a view reads the same in both.
 */
class VbHtmlBundleProvider : TextMateBundleProvider {

    override fun getBundles(): List<TextMateBundleProvider.PluginBundle> {
        val path = PluginManagerCore
            .getPlugin(PluginId.getId("com.basalt.vbrazor"))
            ?.pluginPath
            ?.resolve("lib")
            ?.resolve("textmate")
            ?.resolve("vbhtml")
            ?: return emptyList()

        // Missing rather than broken: a plugin built without the grammar
        // still colours the code, and throwing here would take the whole
        // plugin down for the sake of the markup.
        if (!path.toFile().isDirectory) return emptyList()

        return listOf(TextMateBundleProvider.PluginBundle("Razor (Visual Basic)", path))
    }
}
