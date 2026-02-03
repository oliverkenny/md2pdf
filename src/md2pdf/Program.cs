using System.Diagnostics;
using System.Runtime.InteropServices;
using Markdig;
using Microsoft.Playwright;

namespace Md2Pdf;

internal sealed class Options
{
    public bool ConvertAll { get; set; }
    public bool Recursive { get; set; }
    public string? OutputDirectory { get; set; }
    public bool OpenStyle { get; set; }
    public bool InitStyle { get; set; }
    public string Theme { get; set; } = "default";
    public string Paper { get; set; } = "A4";
    public string? Margins { get; set; }
    public bool Toc { get; set; }
    public bool Verbose { get; set; }
    public bool DryRun { get; set; }
    public List<string> Inputs { get; } = new();
}

internal static class Program
{
    private const string DefaultTemplate = """
<!DOCTYPE html>
<html lang=\"en\">
<head>
  <meta charset=\"utf-8\" />
  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />
  <title>{{title}}</title>
  {{styles}}
  <style>
    body { -webkit-print-color-adjust: exact; print-color-adjust: exact; }
  </style>
</head>
<body>
{{content}}
</body>
</html>
""";

    private const string DefaultThemeCss = """
:root {
  --text-color: #1f2328;
  --link-color: #0969da;
  --code-bg: #f6f8fa;
  --border-color: #d0d7de;
}

@page {
  size: A4;
  margin: 20mm;
}

body {
  font-family: "Segoe UI", "Helvetica Neue", Arial, sans-serif;
  color: var(--text-color);
  line-height: 1.6;
  font-size: 11pt;
}

h1, h2, h3, h4, h5, h6 {
  margin-top: 1.4em;
  margin-bottom: 0.6em;
}

a { color: var(--link-color); text-decoration: none; }

a:hover { text-decoration: underline; }

pre, code {
  font-family: "Cascadia Code", "Consolas", monospace;
  background: var(--code-bg);
}

pre {
  padding: 12px;
  border-radius: 6px;
  border: 1px solid var(--border-color);
  overflow: auto;
}

blockquote {
  border-left: 4px solid var(--border-color);
  padding-left: 12px;
  color: #57606a;
}

table {
  border-collapse: collapse;
  width: 100%;
}

table th, table td {
  border: 1px solid var(--border-color);
  padding: 6px 10px;
}

hr {
  border: none;
  border-top: 1px solid var(--border-color);
  margin: 24px 0;
}
""";

    public static async Task<int> Main(string[] args)
    {
        Options options;
        try
        {
            options = ParseArgs(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        if (options.OpenStyle || options.InitStyle)
        {
            return HandleStyleCommand(options);
        }

        if (options.Inputs.Count == 0 && !options.ConvertAll)
        {
            Console.Error.WriteLine("Specify a markdown file or use -a/--all.");
            return 1;
        }

        var inputs = ResolveInputs(options);
        if (inputs.Count == 0)
        {
            Console.Error.WriteLine("No markdown files found.");
            return 1;
        }

        var themeInfo = EnsureThemeAvailable(options);
        var outputRoot = options.OutputDirectory != null
            ? Path.GetFullPath(options.OutputDirectory)
            : null;

        if (options.DryRun)
        {
            foreach (var input in inputs)
            {
                var outputPath = GetOutputPath(input, outputRoot, options.Recursive);
                Console.WriteLine($"[dry-run] {input} -> {outputPath}");
            }
            return 0;
        }

        await using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });

        foreach (var input in inputs)
        {
            var outputPath = GetOutputPath(input, outputRoot, options.Recursive);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            if (options.Verbose)
            {
                Console.WriteLine($"Converting {input} -> {outputPath}");
            }

            await ConvertFileAsync(browser, input, outputPath, themeInfo, options);
        }

        return 0;
    }

    private static Options ParseArgs(string[] args)
    {
        var options = new Options();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "-a":
                case "--all":
                    options.ConvertAll = true;
                    break;
                case "-r":
                case "--recursive":
                    options.Recursive = true;
                    break;
                case "-o":
                case "--output":
                    options.OutputDirectory = RequireValue(args, ref i, arg);
                    break;
                case "--style":
                    options.OpenStyle = true;
                    break;
                case "--init-style":
                    options.InitStyle = true;
                    break;
                case "--theme":
                    options.Theme = RequireValue(args, ref i, arg);
                    break;
                case "--paper":
                    options.Paper = RequireValue(args, ref i, arg);
                    break;
                case "--margin":
                case "--margins":
                    options.Margins = RequireValue(args, ref i, arg);
                    break;
                case "--toc":
                    options.Toc = true;
                    break;
                case "--verbose":
                    options.Verbose = true;
                    break;
                case "--dry-run":
                    options.DryRun = true;
                    break;
                default:
                    if (arg.StartsWith('-'))
                    {
                        throw new ArgumentException($"Unknown option: {arg}");
                    }
                    options.Inputs.Add(arg);
                    break;
            }
        }

        return options;
    }

    private static string RequireValue(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"Missing value for {option}.");
        }

        index++;
        return args[index];
    }

    private static int HandleStyleCommand(Options options)
    {
        var themeInfo = EnsureThemeAvailable(options);
        if (options.OpenStyle)
        {
            OpenFolder(themeInfo.Path);
            Console.WriteLine(themeInfo.Path);
            return 0;
        }

        Console.WriteLine(themeInfo.Path);
        return 0;
    }

    private static List<string> ResolveInputs(Options options)
    {
        var inputs = new List<string>();
        if (options.ConvertAll)
        {
            var searchOption = options.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            inputs.AddRange(Directory.EnumerateFiles(Directory.GetCurrentDirectory(), "*.md", searchOption));
        }
        inputs.AddRange(options.Inputs.Select(Path.GetFullPath));
        return inputs.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string GetOutputPath(string input, string? outputRoot, bool mirrorDirectories)
    {
        var inputFull = Path.GetFullPath(input);
        var outputFileName = Path.GetFileNameWithoutExtension(inputFull) + ".pdf";
        if (outputRoot == null)
        {
            return Path.Combine(Path.GetDirectoryName(inputFull)!, outputFileName);
        }

        if (!mirrorDirectories)
        {
            return Path.Combine(outputRoot, outputFileName);
        }

        var baseDir = Directory.GetCurrentDirectory();
        var relativePath = Path.GetRelativePath(baseDir, Path.GetDirectoryName(inputFull) ?? baseDir);
        return Path.Combine(outputRoot, relativePath, outputFileName);
    }

    private static ThemeInfo EnsureThemeAvailable(Options options)
    {
        var localThemeDir = Path.Combine(Directory.GetCurrentDirectory(), ".md2pdf", "styles", options.Theme);
        if (Directory.Exists(localThemeDir))
        {
            return LoadTheme(localThemeDir, true);
        }

        var userThemeDir = Path.Combine(GetUserStylesRoot(), options.Theme);
        if (Directory.Exists(userThemeDir))
        {
            return LoadTheme(userThemeDir, false);
        }

        Directory.CreateDirectory(localThemeDir);
        File.WriteAllText(Path.Combine(localThemeDir, "theme.css"), DefaultThemeCss);
        File.WriteAllText(Path.Combine(localThemeDir, "template.html"), DefaultTemplate);
        return LoadTheme(localThemeDir, true);
    }

    private static ThemeInfo LoadTheme(string themePath, bool isLocal)
    {
        var templatePath = Path.Combine(themePath, "template.html");
        var cssPath = Path.Combine(themePath, "theme.css");
        if (!File.Exists(templatePath))
        {
            File.WriteAllText(templatePath, DefaultTemplate);
        }
        if (!File.Exists(cssPath))
        {
            File.WriteAllText(cssPath, DefaultThemeCss);
        }

        var fontsCssPath = Path.Combine(themePath, "fonts.css");
        return new ThemeInfo(themePath, templatePath, cssPath, File.Exists(fontsCssPath) ? fontsCssPath : null, isLocal);
    }

    private static string GetUserStylesRoot()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "md2pdf", "styles");
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "Library", "Application Support", "md2pdf", "styles");
        }

        var config = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(config))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".config", "md2pdf", "styles");
        }

        return Path.Combine(config, "md2pdf", "styles");
    }

    private static async Task ConvertFileAsync(IBrowser browser, string inputPath, string outputPath, ThemeInfo theme, Options options)
    {
        var markdown = await File.ReadAllTextAsync(inputPath);
        if (options.Toc)
        {
            markdown = "[TOC]\n\n" + markdown;
        }

        var pipelineBuilder = new MarkdownPipelineBuilder().UseAdvancedExtensions();
        if (options.Toc)
        {
            pipelineBuilder = pipelineBuilder.UseTableOfContents();
        }

        var pipeline = pipelineBuilder.Build();
        var htmlContent = Markdown.ToHtml(markdown, pipeline);

        var template = await File.ReadAllTextAsync(theme.TemplatePath);
        var styles = $"<link rel=\"stylesheet\" href=\"{ToFileUri(theme.CssPath)}\" />";
        if (!string.IsNullOrWhiteSpace(theme.FontsCssPath))
        {
            styles += $"\n<link rel=\"stylesheet\" href=\"{ToFileUri(theme.FontsCssPath!)}\" />";
        }

        var title = Path.GetFileNameWithoutExtension(inputPath);
        var fullHtml = template
            .Replace("{{content}}", htmlContent)
            .Replace("{{styles}}", styles)
            .Replace("{{title}}", title);

        var tempDir = Path.Combine(Path.GetDirectoryName(inputPath)!, ".md2pdf-tmp");
        Directory.CreateDirectory(tempDir);
        var tempHtmlPath = Path.Combine(tempDir, $"{Guid.NewGuid():N}.html");
        await File.WriteAllTextAsync(tempHtmlPath, fullHtml);

        try
        {
            var page = await browser.NewPageAsync();
            await page.GotoAsync(ToFileUri(tempHtmlPath));
            await page.PdfAsync(new PagePdfOptions
            {
                Path = outputPath,
                Format = options.Paper,
                PrintBackground = true,
                Margin = ParseMargins(options.Margins)
            });
            await page.CloseAsync();
        }
        finally
        {
            File.Delete(tempHtmlPath);
            if (!Directory.EnumerateFileSystemEntries(tempDir).Any())
            {
                Directory.Delete(tempDir);
            }
        }
    }

    private static Margin? ParseMargins(string? margins)
    {
        if (string.IsNullOrWhiteSpace(margins))
        {
            return null;
        }

        var parts = margins.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string top;
        string right;
        string bottom;
        string left;
        if (parts.Length == 1)
        {
            top = right = bottom = left = parts[0];
        }
        else if (parts.Length == 2)
        {
            top = bottom = parts[0];
            right = left = parts[1];
        }
        else if (parts.Length == 3)
        {
            top = parts[0];
            right = left = parts[1];
            bottom = parts[2];
        }
        else if (parts.Length == 4)
        {
            top = parts[0];
            right = parts[1];
            bottom = parts[2];
            left = parts[3];
        }
        else
        {
            throw new ArgumentException("Invalid margin format. Use 1, 2, 3, or 4 values.");
        }

        return new Margin
        {
            Top = top,
            Right = right,
            Bottom = bottom,
            Left = left
        };
    }

    private static string ToFileUri(string path)
    {
        return new Uri(path).AbsoluteUri;
    }

    private static void OpenFolder(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
            return;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Process.Start("open", path);
            return;
        }

        Process.Start("xdg-open", path);
    }
}

internal sealed record ThemeInfo(string Path, string TemplatePath, string CssPath, string? FontsCssPath, bool IsLocal);
