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

		Helper methods to use:
		- MemberResolver.ResolveMember() to validate and resolve member ID
		- InheritanceAnalyzer.GetOverrides() for override chain information
		- MemberResolver.GetMemberSignature() for signature formatting
		- ResponseFormatter.TryExecute() for error handling
		- ResponseFormatter.MemberDetails() for response formatting
		- Additional helper needed: AttributeExtractor for attribute information
		- Additional helper needed: XmlDocumentationProvider for XML docs
		*/
		return "TODO";
	}
}
