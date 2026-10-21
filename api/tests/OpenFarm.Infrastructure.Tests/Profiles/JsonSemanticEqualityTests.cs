using FluentAssertions;
using OpenFarm.Infrastructure.Profiles;

namespace OpenFarm.Infrastructure.Tests.Profiles;

// Postgres jsonb returns its own canonical text, so ingestion must compare what the JSON means, not how it is written.
public class JsonSemanticEqualityTests
{
    [Theory]
    [InlineData("""{"a":1,"b":["x","y"]}""", """{ "b": ["x", "y"],   "a": 1 }""")] // key order and spacing
    [InlineData("""{"a":"1","a":"2"}""", """{"a":"2"}""")] // duplicate keys: the last one wins, as in jsonb
    [InlineData("""{"n":1.0}""", """{"n":1.00}""")] // numbers compare by value
    [InlineData("""{"o":{"k":[true,false,null]}}""", """{"o":{"k":[true,false,null]}}""")]
    public void Equal_SameMeaning_IsTrue(string left, string right) =>
        JsonSemanticEquality.Equal(left, right).Should().BeTrue();

    [Theory]
    [InlineData("""{"a":["x","y"]}""", """{"a":["y","x"]}""")] // array order is meaningful
    [InlineData("""{"a":"1"}""", """{"a":1}""")] // string vs number
    [InlineData("""{"a":1}""", """{"a":1,"b":2}""")]
    [InlineData("""{"a":true}""", """{"a":false}""")]
    [InlineData("""{"a":{"b":"1"}}""", """{"a":{"b":"2"}}""")]
    [InlineData("""{"a":1}""", "not json")]
    public void Equal_DifferentMeaning_IsFalse(string left, string right) =>
        JsonSemanticEquality.Equal(left, right).Should().BeFalse();
}
