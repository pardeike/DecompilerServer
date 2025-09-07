using System.ComponentModel;
using ModelContextProtocol.Server;

namespace DecompilerServer;

public static class GenerateHarmonyPatchSkeletonTool
{
	[McpServerTool, Description("Generate a Harmony patch skeleton for a given member.")]
	public static string GenerateHarmonyPatchSkeleton(string memberId, string patchKinds = "Prefix,Postfix,Transpiler,Finalizer", bool includeReflectionTargeting = true)
	{
		/*
		Behavior:
		- Resolve target and construct a compilable C# snippet that includes:
		  * [HarmonyPatch] targeting Type and method (with overload disambiguation via argument types).
		  * Stubs for requested patchKinds with correct signatures (Prefix/Postfix parameter patterns incl. ref/out, __instance, __result, __state).
		  * If includeReflectionTargeting, show an alternative using AccessTools.Method with parameter type array to bind exact overload.
		- Return { target: MemberSummary, code: string, notes: string[] }.

		Helper methods to use:
		- MemberResolver.ResolveMember() to validate and resolve target member
		- MemberResolver.GetMemberSignature() for generating method signatures
		- ResponseFormatter.TryExecute() for error handling
		- ResponseFormatter.GeneratedCode() for response formatting
		- Additional helper needed: HarmonyCodeGenerator for generating patch skeletons
		*/
		return "TODO";
	}
}
