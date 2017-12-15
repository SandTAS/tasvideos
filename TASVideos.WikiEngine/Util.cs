using System;
using System.IO;
using Newtonsoft.Json;

namespace TASVideos.WikiEngine
{
	public static class Util
	{
		public static string DebugParseWikiPage(string content)
		{
			try
			{
				var results = NewParser.Parse(content);
				return JsonConvert.SerializeObject(results, Formatting.Indented);
			}
			catch (NewParser.SyntaxException e)
			{
				return JsonConvert.SerializeObject(new
				{
					Error = e.Message
				}, Formatting.Indented);
			}
		}
		public static void DebugWriteHtml(string content, TextWriter w)
		{
			try
			{
				var results = NewParser.Parse(content);
				foreach (var r in results)
					r.WriteHtml(w);
			}
			catch (NewParser.SyntaxException e)
			{
				w.Write($"<!-- ERROR {e.Message} -->");
			}
		}

		public static void RenderRazor(string pageName, string content, TextWriter w)
		{
			var results = NewParser.Parse(content);
			w.WriteLine(@"@model WikiPage");
			w.Write(@"@{ Layout = ""/Views/Shared/_WikiLayout.cshtml""; }");

			foreach (var r in results)
				r.WriteHtml(w);
		}
	}
}
