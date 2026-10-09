public class OrderedMapTests
{
    [Test]
    public async Task KeepsInsertionOrderAcrossRemovals()
    {
        var map = new OrderedMap<int>();
        for (var i = 0; i < 10; i++)
        {
            map[$"k{i}"] = i;
        }

        map.Remove("k0");
        map.Remove("k4");
        map.Remove("k9");

        await Assert.That(string.Join(",", map.Keys())).IsEqualTo("k1,k2,k3,k5,k6,k7,k8");
        await Assert.That(string.Join(",", map.Values())).IsEqualTo("1,2,3,5,6,7,8");
        await Assert.That(map.Count).IsEqualTo(7);
    }

    [Test]
    public async Task UpdateKeepsPositionAndReAddGoesLast()
    {
        var map = new OrderedMap<string>
        {
            ["a"] = "1",
            ["b"] = "2",
            ["c"] = "3"
        };

        map["a"] = "updated";
        map.Remove("b");
        map["b"] = "back";

        await Assert.That(string.Join(",", map.Entries().Select(_ => $"{_.Key}={_.Value}")))
            .IsEqualTo("a=updated,c=3,b=back");
    }

    [Test]
    public async Task LookupsReflectRemovals()
    {
        var map = new OrderedMap<int>
        {
            ["a"] = 1,
            ["b"] = 2
        };

        map.Remove("a");
        map.Remove("missing");

        await Assert.That(map.ContainsKey("a")).IsFalse();
        await Assert.That(map.TryGetValue("a", out _)).IsFalse();
        await Assert.That(map.GetValueOrDefault("a")).IsEqualTo(0);
        await Assert.That(map.ContainsKey("b")).IsTrue();
        await Assert.That(map["b"]).IsEqualTo(2);
        await Assert.That(() => map["a"]).Throws<KeyNotFoundException>();
    }

    // Removing from the front is what undoing normalization does to the layout graph's node map. Every
    // remaining key must stay reachable, and in order, through the rebuilds this triggers on the way down
    // and the regrowth afterwards.
    [Test]
    public async Task SurvivesBulkRemovalFromTheFrontAndRegrowth()
    {
        const int count = 5000;
        var map = new OrderedMap<int>();
        for (var i = 0; i < count; i++)
        {
            map[$"k{i}"] = i;
        }

        for (var i = 0; i < count - 3; i++)
        {
            map.Remove($"k{i}");
        }

        await Assert.That(string.Join(",", map.Keys())).IsEqualTo("k4997,k4998,k4999");
        await Assert.That(map["k4998"]).IsEqualTo(4998);

        for (var i = 0; i < 100; i++)
        {
            map[$"n{i}"] = i;
        }

        var keys = map.Keys();
        await Assert.That(keys.Count).IsEqualTo(103);
        await Assert.That(keys[0]).IsEqualTo("k4997");
        await Assert.That(keys[3]).IsEqualTo("n0");
        await Assert.That(keys[^1]).IsEqualTo("n99");
        for (var i = 0; i < 100; i++)
        {
            await Assert.That(map[$"n{i}"]).IsEqualTo(i);
        }
    }

    [Test]
    public async Task EnumeratesLiveEntriesOnly()
    {
        var map = new OrderedMap<int>
        {
            ["a"] = 1,
            ["b"] = 2,
            ["c"] = 3
        };
        map.Remove("b");

        var pairs = new List<string>();
        foreach (var (key, value) in map)
        {
            pairs.Add($"{key}={value}");
        }

        var keys = new List<string>();
        foreach (var key in map.EnumerateKeys())
        {
            keys.Add(key);
        }

        var values = new List<int>();
        foreach (var value in map.EnumerateValues())
        {
            values.Add(value);
        }

        await Assert.That(string.Join(",", pairs)).IsEqualTo("a=1,c=3");
        await Assert.That(string.Join(",", keys)).IsEqualTo("a,c");
        await Assert.That(string.Join(",", values)).IsEqualTo("1,3");
    }

    [Test]
    public async Task EmptyMapBehaves()
    {
        var map = new OrderedMap<int>();

        map.Remove("nothing");

        await Assert.That(map.Count).IsEqualTo(0);
        await Assert.That(map.ContainsKey("a")).IsFalse();
        await Assert.That(map.Keys().Count).IsEqualTo(0);
        await Assert.That(map.ToList().Count).IsEqualTo(0);
    }

    [Test]
    public async Task MutatingWhileEnumeratingThrows()
    {
        var map = new OrderedMap<int>
        {
            ["a"] = 1,
            ["b"] = 2
        };

        await Assert.That(() =>
            {
                foreach (var key in map.EnumerateKeys())
                {
                    map.Remove(key);
                }
            })
            .Throws<InvalidOperationException>();
    }
}
