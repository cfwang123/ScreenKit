namespace ScreenKit;

public sealed class SfJob {
	public long Id;
	public string Name = "";
	public bool ToPhone;
	public int State;
	public long Size;
	public long Done;
	public string Err = "";
	public string StoreRel = "";
	public long Unix;
}

/// <summary>传输任务：等待 / 进行中 / 完成 / 失败，带字节进度。</summary>
public sealed class SendFileJobs {
	public const int Wait = 0, Run = 1, Done = 2, Fail = 3;
	readonly object gate = new();
	readonly List<SfJob> all = new();
	long nextId = 1;
	int lastFire;
	const int MAXKEEP = 100;
	const int FIRE_MS = 80;
	public event Action Changed;

	public SfJob Add(string name, bool toPhone, long size, string storeRel) {
		SfJob j;
		lock (gate) {
			j = new SfJob {
				Id = nextId++,
				Name = name ?? "",
				ToPhone = toPhone,
				State = Wait,
				Size = size < 0 ? 0 : size,
				StoreRel = storeRel ?? "",
				Unix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
			};
			all.Insert(0, j);
			while (all.Count > MAXKEEP) all.RemoveAt(all.Count - 1);
		}
		fire(force: true);
		return j;
	}

	public SfJob FindStore(string rel) {
		rel = (rel ?? "").Replace('\\', '/').Trim('/');
		if (rel.Length == 0) return null;
		lock (gate) {
			return all.FirstOrDefault(x =>
				x.State <= Run
				&& string.Equals(x.StoreRel, rel, StringComparison.OrdinalIgnoreCase));
		}
	}

	public void SetRun(long id) {
		lock (gate) {
			var j = find(id);
			if (j == null) return;
			j.State = Run;
		}
		fire(force: true);
	}

	public void AddDone(long id, int n) {
		if (n <= 0) return;
		lock (gate) {
			var j = find(id);
			if (j == null) return;
			j.Done += n;
			if (j.Size > 0 && j.Done > j.Size) j.Done = j.Size;
			if (j.State == Wait) j.State = Run;
		}
		fire(force: false);
	}

	public void Finish(long id, bool ok, string err) {
		lock (gate) {
			var j = find(id);
			if (j == null) return;
			j.State = ok ? Done : Fail;
			j.Err = err ?? "";
			if (ok && j.Size > 0) j.Done = j.Size;
			j.Unix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		}
		fire(force: true);
	}

	public List<SfJob> Snapshot() {
		lock (gate) {
			return all.Select(copy).ToList();
		}
	}

	SfJob find(long id) => all.FirstOrDefault(x => x.Id == id);

	static SfJob copy(SfJob j) => new() {
		Id = j.Id,
		Name = j.Name,
		ToPhone = j.ToPhone,
		State = j.State,
		Size = j.Size,
		Done = j.Done,
		Err = j.Err,
		StoreRel = j.StoreRel,
		Unix = j.Unix,
	};

	void fire(bool force) {
		if (!force) {
			var now = Environment.TickCount;
			if (unchecked(now - lastFire) < FIRE_MS) return;
			lastFire = now;
		}
		else lastFire = Environment.TickCount;
		try { Changed?.Invoke(); } catch { }
	}
}
