using System.Diagnostics;

namespace LocalCompanion.Services.LlamaNative;

internal enum LlamaDraftMaxFlag
{
    None,
    SpecNMax,
    LegacyDraftMax,
}

internal readonly record struct LlamaDraftCliSupport(bool CanAttachModel, LlamaDraftMaxFlag MaxFlag)
{
    internal static LlamaDraftCliSupport AssumeCurrent { get; } = new(true, LlamaDraftMaxFlag.SpecNMax);

    internal static LlamaDraftCliSupport None { get; } = new(false, LlamaDraftMaxFlag.None);

    internal string? MaxTokensArg => MaxFlag switch
    {
        LlamaDraftMaxFlag.SpecNMax => "--spec-draft-n-max",
        LlamaDraftMaxFlag.LegacyDraftMax => "--draft-max",
        _ => null,
    };
}

/// <summary>今の llama-server が下書き引数を受け付けるかだけを見る。</summary>
internal static class LlamaDraftCli
{
    internal static LlamaDraftCliSupport Probe(string exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            return LlamaDraftCliSupport.None;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.ArgumentList.Add("-h");

            using var proc = Process.Start(psi);
            if (proc is null)
                return LlamaDraftCliSupport.None;

            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();
            if (!proc.WaitForExit(8000))
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
                return LlamaDraftCliSupport.None;
            }

            var help = stdoutTask.GetAwaiter().GetResult() + "\n" + stderrTask.GetAwaiter().GetResult();
            return ParseHelp(help);
        }
        catch
        {
            return LlamaDraftCliSupport.None;
        }
    }

    internal static LlamaDraftCliSupport ParseHelp(string helpText)
    {
        if (string.IsNullOrWhiteSpace(helpText))
            return LlamaDraftCliSupport.None;

        var canAttach = HasLiveOption(helpText, "--spec-draft-model")
            || HasLiveOption(helpText, "--model-draft")
            || HasLiveOption(helpText, "-md");
        if (!canAttach)
            return LlamaDraftCliSupport.None;

        if (HasLiveOption(helpText, "--spec-draft-n-max"))
            return new LlamaDraftCliSupport(true, LlamaDraftMaxFlag.SpecNMax);
        if (HasLiveOption(helpText, "--draft-max"))
            return new LlamaDraftCliSupport(true, LlamaDraftMaxFlag.LegacyDraftMax);
        return new LlamaDraftCliSupport(true, LlamaDraftMaxFlag.None);
    }

    internal static bool LooksLikeCliArgRejected(string logText)
    {
        if (string.IsNullOrWhiteSpace(logText))
            return false;

        return logText.Contains("has been removed", StringComparison.OrdinalIgnoreCase)
            || logText.Contains("error while handling argument", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool RemoveDraftArgs(List<string> args)
    {
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "-md",
            "--model-draft",
            "--spec-draft-model",
            "--spec-draft-n-max",
            "--draft-max",
            "--draft-n",
            "--draft",
        };

        var removed = false;
        var i = 0;
        while (i < args.Count)
        {
            if (!flags.Contains(args[i]))
            {
                i++;
                continue;
            }

            args.RemoveAt(i);
            if (i < args.Count)
                args.RemoveAt(i);
            removed = true;
        }

        return removed;
    }

    private static bool HasLiveOption(string help, string flag)
    {
        foreach (var line in help.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!LineDeclaresFlag(line, flag))
                continue;
            if (line.Contains("has been removed", StringComparison.OrdinalIgnoreCase))
                continue;
            return true;
        }

        return false;
    }

    private static bool LineDeclaresFlag(string line, string flag)
    {
        var text = line.TrimStart();
        if (text.Length == 0 || text[0] != '-')
            return false;

        var namesEnd = text.IndexOf("  ", StringComparison.Ordinal);
        var names = namesEnd > 0 ? text[..namesEnd] : text;
        foreach (var part in names.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var space = part.IndexOf(' ');
            var token = space > 0 ? part[..space] : part;
            if (token.Equals(flag, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
