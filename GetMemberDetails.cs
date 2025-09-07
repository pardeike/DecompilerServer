using System.ComponentModel;
using ModelContextProtocol.Server;

namespace DecompilerServer;

public static class GetMemberDetailsTool
{
	[McpServerTool, Description("Detailed metadata for a member: attributes, docs, inheritance links.")]
	public static string GetMemberDetails(string memberId)
	{
		/*
		Behavior:
		- Resolve member. Collect:
		  * Attributes (full names, constructor args where simple)
		  * XmlDoc (summary only or full XML)
		  * BaseDefinitionId (for virtuals)
		  * OverrideIds (if this overrides others)
		  * ImplementorIds (if interface)
		- Return MemberDetails.
		*/
		return "TODO";
	}
}
