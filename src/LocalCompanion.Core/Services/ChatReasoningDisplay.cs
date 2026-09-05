namespace LocalCompanion.Services;

/// <summary>推論は流れているあいだだけ見せ、本文が出たら消す。</summary>
public static class ChatReasoningDisplay
{
    public static bool ShowPanel(bool liveStream, string? bodyText)
    {
        if (!liveStream)
            return false;
        if (!string.IsNullOrWhiteSpace(bodyText))
            return false;
        return true;
    }
}
