using System;

namespace System.Text.Json.Serialization;

public enum JsonIgnoreCondition { Never = 0, Always, WhenWritingDefault, WhenWritingNull }

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public sealed class JsonIgnoreAttribute : Attribute {
	public JsonIgnoreCondition Condition { get; set; } = JsonIgnoreCondition.Always;
}
