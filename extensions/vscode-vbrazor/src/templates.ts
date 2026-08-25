import * as fs from 'fs/promises';
import * as path from 'path';

/** A project the extension can create. */
export interface ProjectTemplate {
    readonly id: string;
    readonly label: string;
    readonly description: string;
}

export const templates: readonly ProjectTemplate[] = [
    {
        id: 'webapi',
        label: 'ASP.NET Core Web API',
        description: 'HTTP service returning JSON, written in Visual Basic.'
    },
    {
        id: 'razorpages',
        label: 'ASP.NET Core Razor Pages',
        description: 'Page-based web application using .vbhtml views.'
    },
    {
        id: 'mvc',
        label: 'ASP.NET Core MVC',
        description: 'Controllers and .vbhtml views, in Visual Basic.'
    }
];

/**
 * Writes a project to disk.
 *
 * The files are written here rather than by "dotnet new" because no official
 * Visual Basic web templates exist — which is the gap this whole extension
 * addresses.
 */
export async function createProject(
    templateId: string, directory: string, name: string): Promise<string> {

    await fs.mkdir(directory, { recursive: true });

    const projectPath = path.join(directory, `${name}.vbproj`);

    await fs.writeFile(projectPath, projectFile(templateId), 'utf8');
    await fs.writeFile(path.join(directory, 'Program.vb'), programFile(templateId, name), 'utf8');

    if (templateId === 'razorpages') {
        await fs.mkdir(path.join(directory, 'Pages'), { recursive: true });
        await fs.writeFile(
            path.join(directory, 'Pages', 'Index.vbhtml'), indexPage(name), 'utf8');
    }

    if (templateId === 'mvc') {
        await fs.mkdir(path.join(directory, 'Views', 'Home'), { recursive: true });
        await fs.writeFile(
            path.join(directory, 'Views', 'Home', 'Index.vbhtml'), indexPage(name), 'utf8');
    }

    return projectPath;
}

function projectFile(templateId: string): string {
    const web = templateId !== 'console';

    return `<Project Sdk="Microsoft.NET.Sdk${web ? '.Web' : ''}">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <!-- Empty on purpose: otherwise the project name is prepended to every
         namespace declared in the code. -->
    <RootNamespace></RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <!-- Compiles the .vbhtml views, which ASP.NET Core cannot do on its own. -->
    <PackageReference Include="Basalt.Razor.Vb" Version="1.0.0" />
  </ItemGroup>

</Project>
`;
}

function programFile(templateId: string, name: string): string {
    if (templateId === 'webapi') {
        return `Imports Microsoft.AspNetCore.Builder

Module Program
    Sub Main(args As String())
        Dim builder = WebApplication.CreateBuilder(args)
        Dim app = builder.Build()

        app.MapGet("/", Function() "Hello from ${name}.")

        app.Run()
    End Sub
End Module
`;
    }

    return `Imports Microsoft.AspNetCore.Builder

Module Program
    Sub Main(args As String())
        Dim builder = WebApplication.CreateBuilder(args)
        Dim app = builder.Build()

        ' The view is compiled by Basalt.Razor.Vb and rendered here.
        app.MapGet("/", Function()
                            Dim view As New ${templateId === 'mvc' ? 'Views.Home.Index' : 'Pages.Index'}()
                            view.Model = "${name}"
                            Return Results.Content(view.Render(), "text/html")
                        End Function)

        app.Run()
    End Sub
End Module
`;
}

function indexPage(name: string): string {
    return `@ModelType String

<!DOCTYPE html>
<html>
<head>
    <title>${name}</title>
</head>
<body>
    <h1>Hello from @Model</h1>
    <p>This page is a Razor view written in Visual Basic.</p>

    @Code
        Dim items = New String() {"first", "second", "third"}
    End Code

    <ul>
        @For Each item In items
            <li>@item</li>
        Next
    </ul>
</body>
</html>
`;
}
