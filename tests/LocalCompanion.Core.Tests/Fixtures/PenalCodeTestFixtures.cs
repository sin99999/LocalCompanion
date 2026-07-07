using System.Text;

namespace LocalCompanion.Core.Tests.Fixtures;

/// <summary>刑法 .md の形状を再現するテスト用サンプル（個人ファイル非依存）。</summary>
internal static class PenalCodeTestFixtures
{
    public const int DefaultArticleCount = 55;

    public static string BuildMarkdown(int articleCount = DefaultArticleCount)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# 刑法");
        sb.AppendLine();
        sb.AppendLine("## 第1編 総則");
        sb.AppendLine();
        sb.AppendLine("### 第1章 通則");
        sb.AppendLine();

        for (var i = 1; i <= articleCount; i++)
        {
            switch (i)
            {
                case 7:
                    sb.AppendLine("#### 第7条（定義）");
                    sb.AppendLine();
                    sb.AppendLine("この法律において「公務員」とは、国又は地方公共団体の職員その他法令により公務に従事する議員、委員その他の職員をいう。");
                    break;
                case 8:
                    sb.AppendLine("#### 第8条（他の法令の罪に対する適用）");
                    sb.AppendLine();
                    sb.AppendLine("この編の規定は、他の法令の罪についても、適用する。ただし、その法令に特別の規定があるときは、この限りでない。");
                    break;
                case 54:
                    sb.AppendLine("#### 第54条（一個の行為）");
                    sb.AppendLine();
                    sb.AppendLine("一個の行為により、二個以上の罪を犯した者は、最も重い刑により処断する。");
                    break;
                default:
                    sb.AppendLine($"#### 第{i}条");
                    sb.AppendLine();
                    sb.AppendLine("本条の内容。一個の行為に関する規定の例示。");
                    break;
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>環境変数 LOCPENALCODE_MD が指す実ファイル（任意）。</summary>
    public static string? TryLoadOptionalLocalFile()
    {
        var fromEnv = Environment.GetEnvironmentVariable("LOCPENALCODE_MD");
        if (!string.IsNullOrWhiteSpace(fromEnv) && File.Exists(fromEnv))
            return File.ReadAllText(fromEnv);

        return null;
    }
}
