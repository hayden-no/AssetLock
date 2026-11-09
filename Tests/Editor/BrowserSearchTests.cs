using System;
using AssetLock.Editor.UI;
using NUnit.Framework;

namespace AssetLock.Editor.Tests
{
	[TestFixture, Category("AssetLockUnit")]
	internal class BrowserSearchTests
	{
		[TestCase("", "", true), TestCase("", "*", true), TestCase("", "*a", false), TestCase("abc", "", false),
		 TestCase("abc", "a*c", true), TestCase("ac", "a*c", true), TestCase("abcd", "a*c", false),
		 TestCase("abcd", "*b*D", true), TestCase("abc", "**a**c**", true), TestCase("AbC", "a*c", true),
		 TestCase("Élan", "é*", true), TestCase("a?c", "a?c", true), TestCase("abc", "a?c", false),
		 TestCase("abcccd", "*c*d", true)]
		public void WildcardMatchesWholeFieldIgnoringCase(string input, string pattern, bool expected) =>
			Assert.That(ALGUI.IsMatchWithStarWildcard(input.AsSpan(), pattern.AsSpan()), Is.EqualTo(expected));

		[Test]
		public void PlainSearchRemainsCaseInsensitiveSubstring()
		{
			Assert.That(ALGUI.MatchesBrowserSearch("Assets/Folder/House.prefab", "folder"), Is.True);
			Assert.That(ALGUI.MatchesBrowserSearch("Assets/Folder/House.prefab", "other"), Is.False);
			Assert.That(ALGUI.MatchesBrowserSearch(null, "*"), Is.False);
		}
	}
}