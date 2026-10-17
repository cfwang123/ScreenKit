using System.Collections.Generic;

namespace ScreenKit;

enum Lang { Wubi, Ja, Ko }

enum View { Wubi, Ja, Ko, En }

class Cand {
	public string Text = "";
	public string Raw = "";
	public string Code = "";
	public string Extra = "";
	public string Comment = "";
	public bool Pinned;
	public int Picks;
	public int Rank;
}

class Outcome {
	public bool Eat;
	public List<string> Texts = new List<string>();
	public bool OpenDict;
}
