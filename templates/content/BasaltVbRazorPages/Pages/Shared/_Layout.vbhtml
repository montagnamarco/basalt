<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>@ViewData("Title") - BasaltVbRazorPages</title>
    <link rel="stylesheet" href="@Url.Content("~/css/site.css")" />
</head>
<body>
    <header>
        <nav>
            <a asp-page="/Index">Home</a>
            <a asp-page="/Privacy">Privacy</a>
        </nav>
    </header>

    <main>
        @RenderBody()
    </main>

    <footer>
        &copy; BasaltVbRazorPages
    </footer>

    @RenderSection("Scripts", False)
</body>
</html>
