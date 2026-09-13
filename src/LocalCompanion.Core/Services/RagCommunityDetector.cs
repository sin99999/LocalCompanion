using LocalCompanion.Models;

namespace LocalCompanion.Services;

/// <summary>
/// 決定的なラベル伝播（ノード id 昇順・同点は小さいラベル）でコミュニティを作る。依存なし。
/// level 0 を縮約してもう一段回し、level 1（親）を作る。孤立ノードは 1 ノード 1 コミュニティ（連結成分と同じ）。
/// ラベルは次数上位 3 概念の語＋最頻見出し（LLM なし）。
/// 実 DB では「懲役」「罰金」のようなハブ語が全条文を 1 束に溶かした（2254 概念中 1344 が 1 束）ので、
/// 隣接数でハブを減衰し、束の大きさに上限を置く（level 0 は <see cref="Level0MaxSize"/> 概念、level 1 は <see cref="Level1MaxSize"/>）。
/// </summary>
internal static class RagCommunityDetector
{
    public const int MaxIterations = 30;

    /// <summary>level 0 の 1 束に入る概念の上限（数チャンク分の局所性を保つ）。</summary>
    public const int Level0MaxSize = 30;

    /// <summary>level 1 の 1 束に入る概念の上限。</summary>
    public const int Level1MaxSize = 150;

    public sealed record Node(long Id, string Term);

    public static IReadOnlyList<RagCommunity> Detect(
        IReadOnlyList<Node> nodes,
        IReadOnlyList<(long A, long B, double Weight)> edges,
        IReadOnlyDictionary<long, string>? topHeaderPerConcept = null)
    {
        if (nodes.Count == 0)
            return Array.Empty<RagCommunity>();

        var nodeIds = nodes.Select(n => n.Id).OrderBy(id => id).ToList();
        var adjacency = BuildAdjacency(nodeIds, edges);
        var level0 = Propagate(nodeIds, adjacency, nodeIds.ToDictionary(id => id, _ => 1), Level0MaxSize);

        // level 0 の束をノードにして縮約し、もう一段（超ノードの大きさは中の概念数）
        var groups0 = level0.GroupBy(kv => kv.Value).OrderBy(g => g.Key).ToList();
        var superIds = groups0.Select(g => g.Key).OrderBy(id => id).ToList();
        var superSizes = groups0.ToDictionary(g => g.Key, g => g.Count());
        var superEdges = ContractEdges(edges, level0);
        var level1 = Propagate(superIds, BuildAdjacency(superIds, superEdges), superSizes, Level1MaxSize);

        var termOf = nodes.ToDictionary(n => n.Id, n => n.Term);
        var degree = Degrees(edges);
        var result = new List<RagCommunity>();

        // id は level 0 を 1..N、level 1 を N+1.. と連番。1000 起点だと孤立が多い資料で交差する
        var level1Ids = level1.Values.Distinct().OrderBy(v => v)
            .Select((v, i) => (v, Id: (long)(groups0.Count + 1 + i)))
            .ToDictionary(x => x.v, x => x.Id);
        var level1Members = new Dictionary<long, List<long>>();

        var next0 = 0L;
        foreach (var group in groups0)
        {
            next0++;
            var members = group.Select(kv => kv.Key).OrderBy(id => id).ToList();
            var parent = level1Ids[level1[group.Key]];
            result.Add(new RagCommunity(next0, 0, parent, Label(members, termOf, degree, topHeaderPerConcept), members.Count, members));
            if (!level1Members.TryGetValue(parent, out var list))
                level1Members[parent] = list = new List<long>();
            list.AddRange(members);
        }

        foreach (var (parentId, members) in level1Members.OrderBy(kv => kv.Key))
        {
            var sorted = members.OrderBy(id => id).ToList();
            result.Add(new RagCommunity(parentId, 1, null, Label(sorted, termOf, degree, topHeaderPerConcept), sorted.Count, sorted));
        }

        return result;
    }

    /// <param name="weightOf">ノードの大きさ（level 0 は 1、level 1 は中の概念数）。束の上限判定に使う。</param>
    /// <param name="maxSize">1 束に入る概念数の上限。満杯の束へは移らない（次点へ）。</param>
    private static Dictionary<long, long> Propagate(
        IReadOnlyList<long> ids,
        Dictionary<long, List<(long To, double W)>> adjacency,
        Dictionary<long, int> weightOf,
        int maxSize)
    {
        var label = ids.ToDictionary(id => id, id => id);
        var size = ids.ToDictionary(id => id, id => weightOf[id]);

        // ハブ減衰: 隣接が多いノードの票は薄める（「懲役」「罰金」が全条文を 1 束にしないため）
        var damp = ids.ToDictionary(id => id, id => 1.0 / Math.Log(2 + (adjacency.TryGetValue(id, out var n) ? n.Count : 0)));

        for (var iteration = 0; iteration < MaxIterations; iteration++)
        {
            var changed = false;
            foreach (var id in ids)
            {
                if (!adjacency.TryGetValue(id, out var neighbors) || neighbors.Count == 0)
                    continue;

                var score = new Dictionary<long, double>();
                foreach (var (to, w) in neighbors)
                    score[label[to]] = score.GetValueOrDefault(label[to]) + w * damp[to];

                // 自分の今のラベルにも少し票を入れて振動を止める
                var current = label[id];
                score[current] = score.GetValueOrDefault(current) + 0.5;

                foreach (var candidate in score.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).Select(kv => kv.Key))
                {
                    if (candidate == current)
                        break;
                    if (size[candidate] + weightOf[id] > maxSize)
                        continue;

                    size[current] -= weightOf[id];
                    size[candidate] += weightOf[id];
                    label[id] = candidate;
                    changed = true;
                    break;
                }
            }

            if (!changed)
                break;
        }

        return label;
    }

    private static Dictionary<long, List<(long To, double W)>> BuildAdjacency(
        IReadOnlyList<long> ids,
        IReadOnlyList<(long A, long B, double Weight)> edges)
    {
        var known = new HashSet<long>(ids);
        var adjacency = new Dictionary<long, List<(long, double)>>();
        foreach (var (a, b, w) in edges)
        {
            if (a == b || !known.Contains(a) || !known.Contains(b))
                continue;
            Add(adjacency, a, b, w);
            Add(adjacency, b, a, w);
        }

        return adjacency;

        static void Add(Dictionary<long, List<(long, double)>> map, long from, long to, double w)
        {
            if (!map.TryGetValue(from, out var list))
                map[from] = list = new List<(long, double)>();
            list.Add((to, w));
        }
    }

    private static List<(long A, long B, double Weight)> ContractEdges(
        IReadOnlyList<(long A, long B, double Weight)> edges,
        Dictionary<long, long> label)
    {
        var sum = new Dictionary<(long, long), double>();
        foreach (var (a, b, w) in edges)
        {
            if (!label.TryGetValue(a, out var la) || !label.TryGetValue(b, out var lb) || la == lb)
                continue;
            var key = la < lb ? (la, lb) : (lb, la);
            sum[key] = sum.GetValueOrDefault(key) + w;
        }

        return sum.Select(kv => (kv.Key.Item1, kv.Key.Item2, kv.Value)).ToList();
    }

    private static Dictionary<long, double> Degrees(IReadOnlyList<(long A, long B, double Weight)> edges)
    {
        var degree = new Dictionary<long, double>();
        foreach (var (a, b, w) in edges)
        {
            degree[a] = degree.GetValueOrDefault(a) + w;
            degree[b] = degree.GetValueOrDefault(b) + w;
        }

        return degree;
    }

    private static string Label(
        IReadOnlyList<long> members,
        Dictionary<long, string> termOf,
        Dictionary<long, double> degree,
        IReadOnlyDictionary<long, string>? topHeaderPerConcept)
    {
        var top = members
            .OrderByDescending(id => degree.GetValueOrDefault(id))
            .ThenBy(id => id)
            .Take(3)
            .Select(id => termOf.GetValueOrDefault(id, id.ToString()))
            .ToList();

        var header = topHeaderPerConcept is null
            ? null
            : members
                .Select(id => topHeaderPerConcept.GetValueOrDefault(id))
                .Where(h => !string.IsNullOrWhiteSpace(h))
                .GroupBy(h => h!, StringComparer.Ordinal)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => g.Key)
                .FirstOrDefault();

        var label = string.Join("・", top);
        return string.IsNullOrEmpty(header) ? label : $"{label}（{header}）";
    }
}
