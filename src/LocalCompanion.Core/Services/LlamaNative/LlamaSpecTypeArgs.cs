namespace LocalCompanion.Services.LlamaNative;

/// <summary>ファイル名が MTP のとき、llama-server に投機デコードの引数を足す。</summary>
internal static class LlamaSpecTypeArgs
{
    internal const string DraftMtp = "draft-mtp";

    internal static void AppendDraftMtp(List<string> args, bool enabled)
    {
        if (!enabled)
            return;

        for (var i = 0; i < args.Count - 1; i++)
        {
            if (string.Equals(args[i], "--spec-type", StringComparison.OrdinalIgnoreCase)
                && string.Equals(args[i + 1], DraftMtp, StringComparison.OrdinalIgnoreCase))
                return;
        }

        args.Add("--spec-type");
        args.Add(DraftMtp);
    }
}
