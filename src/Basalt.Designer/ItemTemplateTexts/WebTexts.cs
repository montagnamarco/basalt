using System.Net;

namespace Basalt.Designer;

/// <summary>The plain web files: markup, style, script and data, with nothing Basalt-specific in them.</summary>
internal static class WebTexts
{
    public static string HtmlPage(string name)
    {
        var title = WebUtility.HtmlEncode(name);

        return $"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
                <meta charset="utf-8" />
                <meta name="viewport" content="width=device-width, initial-scale=1.0" />
                <title>{title}</title>
            </head>
            <body>
                <h1>{title}</h1>
            </body>
            </html>

            """;
    }

    public static string StyleSheet(string name) => $$"""
        /* {{name}} */

        body {
            margin: 0;
            font-family: system-ui, sans-serif;
        }

        """;

    public static string JavaScript(string name) => $"""
        "use strict";

        // {name}

        """;

    public static string Json() => """
        {
        }

        """;
}
