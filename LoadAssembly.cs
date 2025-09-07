using System.ComponentModel;
using ModelContextProtocol.Server;

namespace DecompilerServer;

public static class LoadAssemblyTool
{
	[McpServerTool, Description("Load or reload the Assembly-CSharp.dll and build minimal indexes.")]
	public static string LoadAssembly(
		string gameDir,
		string assemblyFile = "Assembly-CSharp.dll",
		string[]? additionalSearchDirs = null,
		bool rebuildIndex = true)
	{
		/*
		Goal: Initialize or refresh the global decompiler context.

		Inputs:
		- gameDir: absolute path to Game's install root (contains Game*_Data/Managed).
		- assemblyFile: relative or absolute path to Assembly-CSharp.dll.
		- additionalSearchDirs: optional extra directories to add to resolver.
		- rebuildIndex: if true, drop caches and rebuild minimal indexes.

		Behavior:
		- Validate paths and file existence. Return error if missing.
		- Dispose existing context if already loaded.
		- Create UniversalAssemblyResolver and add search dirs (Managed, MonoBleedingEdge, Unity, plus additionalSearchDirs).
		- Load PEFile, TypeSystem, and CSharpDecompiler.
		- Record MVID and build minimal indexes: namespaces, type name map, simple member name map.
		- Return:
		  {
		    status: "ok",
		    mvid: "<32hex>",
		    assemblyPath: "<resolved>",
		    types: <count>,
		    methods: <count-estimate>,
		    namespaces: <count>,
		    warmed: false
		  }
		*/
		return "TODO";
	}
}