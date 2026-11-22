using KScript.Metadata;
using KScript.Runtime;

namespace KScript.Tests;

/// <summary>
/// What phone, country, currency, locale, iban, card and money declare on the NativeType contract: the operators,
/// orders and truth tests they take, the errors for those they don't, and that their old behaviour still works.
/// </summary>
public class NativeOperatorsPeopleAndMoneyTests
{
    private static string Code(string code) => ScriptRunner.Run(code, path: Path.Combine(Path.GetTempPath(), "native-operators-money-test.kscript")).TrimEnd('\n');

    private static string Fails(string code) => Assert.Throws<KScriptException>(() => Code(code)).Message;

    [Theory]
    [InlineData("phone")]
    [InlineData("country")]
    [InlineData("currency")]
    [InlineData("locale")]
    [InlineData("iban")]
    [InlineData("card")]
    [InlineData("money")]
    public void EachTypeIsOnTheContract(string name)
    {
        NativeType type = NativeTypeRegistry.Get(name);
        Assert.NotNull(type);
        Assert.Contains(type, NativeTypeRegistry.BuiltIn);
        Assert.Equal(name, NativeTypes.Get(name).Name);
    }

    // ---- money: reading and tidying -------------------------------------------------------------------------------

    [Theory]
    [InlineData("'GBP 9.5'", "GBP 9.50")]
    [InlineData("'gbp 9.50'", "GBP 9.50")]
    [InlineData("'GBP9.50'", "GBP 9.50")]
    [InlineData("'9.50 GBP'", "GBP 9.50")]
    [InlineData("'9.5gbp'", "GBP 9.50")]
    [InlineData("'£9.50'", "GBP 9.50")]
    [InlineData("'$12'", "USD 12.00")]
    [InlineData("'€3.5'", "EUR 3.50")]
    [InlineData("'¥1200'", "JPY 1200")]
    [InlineData("'JPY 1,200'", "JPY 1200")]
    [InlineData("'USD 1,234,567.89'", "USD 1234567.89")]
    [InlineData("'-£5'", "GBP -5.00")]
    [InlineData("'£-5'", "GBP -5.00")]
    [InlineData("'GBP -0.50'", "GBP -0.50")]
    [InlineData("'-0.50 GBP'", "GBP -0.50")]
    [InlineData("'KWD 1.5'", "KWD 1.500")]
    public void MoneyIsReadAndTidied(string value, string expected)
    {
        Assert.Equal(expected, Code($"x: money = {value}\necho $x"));
        Assert.Equal("money", Code($"x: money = {value}\necho $x->type()"));
    }

    [Theory]
    [InlineData("'9.50'", "needs a currency")]
    [InlineData("'€3,50'", "the decimal point is .")]
    [InlineData("'GBP 1,23.00'", "the decimal point is .")]
    [InlineData("'GBP 9.505'", "at most 2 decimal places")]
    [InlineData("'JPY 12.5'", "whole numbers")]
    [InlineData("'XYZ 12'", "XYZ isn't an ISO 4217 currency code")]
    [InlineData("'GBP'", "write it as a currency code and an amount")]
    [InlineData("'-£-5'", "two minus signs")]
    [InlineData("''", "it is empty")]
    public void MoneyOfTheWrongFormFails(string value, string message)
    {
        string error = Fails($"x: money = {value}\necho $x");
        Assert.Contains("needs a money", error);
        Assert.Contains(message, error);
    }

    [Fact]
    public void MoneyHasItsFieldsAndMethods()
    {
        Assert.Equal("9.5 GBP British Pound", Code("x: money = 'GBP 9.50'\necho \"{ $x.amount } { $x.currency } { $x.currency.name }\""));
        Assert.Equal("currency number", Code("x: money = '£9.50'\necho \"{ $x.currency->type() } { $x.amount->type() }\""));
        Assert.Equal("£1,234.50", Code("x: money = 'GBP 1234.5'\necho $x->format('en')"));
        Assert.Equal("1 234,50 €", Code("x: money = 'EUR 1234.5'\necho $x->format('fr')").Replace(' ', ' ').Replace(' ', ' '));
        Assert.Equal("¥1,200", Code("x: money = 'JPY 1200'\necho $x->format('en')"));
    }

    [Theory]
    [InlineData("'GBP 10'", "split(3)", "GBP 3.34, GBP 3.33, GBP 3.33")]
    [InlineData("'GBP 0.05'", "split(2)", "GBP 0.03, GBP 0.02")]
    [InlineData("'GBP -10'", "split(3)", "GBP -3.34, GBP -3.33, GBP -3.33")]
    [InlineData("'JPY 100'", "split(3)", "JPY 34, JPY 33, JPY 33")]
    [InlineData("'GBP 10'", "allocate([1, 1, 2])", "GBP 2.50, GBP 2.50, GBP 5.00")]
    [InlineData("'GBP 0.05'", "allocate([3, 7])", "GBP 0.02, GBP 0.03")]
    [InlineData("'GBP 100'", "allocate([1, 0])", "GBP 100.00, GBP 0.00")]
    public void SplittingKeepsEveryPenny(string value, string call, string expected)
    {
        Assert.Equal(expected, Code($"x: money = {value}\necho $x->{call}"));
        Assert.Equal(Code($"x: money = {value}\necho $x"),
            Code($"x: money = {value}\nparts = $x->{call}\ntotal = $parts[0]\nfor p in $parts->slice(1) {{ total = $total + $p }}\necho $total"));
    }

    [Theory]
    [InlineData("split(0)", "whole number of parts")]
    [InlineData("split(1.5)", "whole number of parts")]
    [InlineData("allocate([])", "list of ratios")]
    [InlineData("allocate([0, 0])", "at least one ratio above 0")]
    [InlineData("allocate([1, -1])", "numbers of 0 or more")]
    public void SplittingNeedsSensibleParts(string call, string message)
    {
        Assert.Contains(message, Fails($"x: money = 'GBP 10'\necho $x->{call}"));
    }

    // ---- money: operators ------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("$a + $b", "GBP 10.00")]
    [InlineData("$a - $b", "GBP 9.00")]
    [InlineData("$a + 'GBP 0.25'", "GBP 9.75")]
    [InlineData("$a * 3", "GBP 28.50")]
    [InlineData("3 * $a", "GBP 28.50")]
    [InlineData("$a * 0.333", "GBP 3.16")]
    [InlineData("$a / 4", "GBP 2.38")]
    [InlineData("$a / $b", "19")]
    [InlineData("-$a", "GBP -9.50")]
    [InlineData("-(-$a)", "GBP 9.50")]
    [InlineData("($a / $b)->type()", "number")]
    [InlineData("($a * 2)->type()", "money")]
    public void MoneyDoesArithmeticInOneCurrency(string expression, string expected)
    {
        Assert.Equal(expected, Code($"a: money = 'GBP 9.50'\nb: money = 'GBP 0.50'\necho {expression}"));
    }

    [Fact]
    public void MoneyAddsExactly()
    {
        // Decimal inside: 0.10 + 0.20 is 0.30, not 0.30000000000000004.
        Assert.Equal("USD 0.30 true", Code("a: money = '$0.10'\nb: money = '$0.20'\nc = $a + $b\necho \"$c { $c == '$0.30' }\""));
        Assert.Equal("0.3", Code("a: money = '$0.10'\nb: money = '$0.20'\necho ($a + $b).amount"));
    }

    [Theory]
    [InlineData("$a + $u", "Can't add GBP 9.50 and USD 1.00: they are in different currencies")]
    [InlineData("$a - $u", "Can't subtract GBP 9.50 and USD 1.00")]
    [InlineData("$a / $u", "Can't divide GBP 9.50 and USD 1.00")]
    [InlineData("$a < $u", "Can't compare GBP 9.50 and USD 1.00")]
    public void MixingCurrenciesIsAnError(string expression, string message)
    {
        Assert.Contains(message, Fails($"a: money = 'GBP 9.50'\nu: money = 'USD 1'\necho {expression}"));
    }

    [Theory]
    [InlineData("$a + 1", "With money it takes")]
    [InlineData("1 + $a", "With money it takes")]
    [InlineData("$a * $a", "With money it takes")]
    [InlineData("$a % 2", "Money values have no '%'")]
    [InlineData("$a / 0", "Division by zero")]
    [InlineData("$a / ($a - $a)", "Division by zero")]
    public void OperatorsMoneyDoesNotTakeAreErrors(string expression, string message)
    {
        Assert.Contains(message, Fails($"a: money = 'GBP 9.50'\necho {expression}"));
    }

    [Theory]
    [InlineData("$a < $b", "false")]
    [InlineData("$a > $b", "true")]
    [InlineData("$a >= 'GBP 9.5'", "true")]
    [InlineData("$a <= '£9.49'", "false")]
    [InlineData("$a == '£9.50'", "true")]
    [InlineData("$a == '9.50 GBP'", "true")]
    [InlineData("$a == 'USD 9.50'", "false")]
    [InlineData("$a != $b", "true")]
    [InlineData("[$a, $b, -$b]->sort()", "GBP -0.50, GBP 0.50, GBP 9.50")]
    public void MoneyComparesInOneCurrency(string expression, string expected)
    {
        Assert.Equal(expected, Code($"a: money = 'GBP 9.50'\nb: money = 'GBP 0.50'\necho {expression}"));
    }

    [Theory]
    [InlineData("'GBP 0'", "no")]
    [InlineData("'GBP 0.00'", "no")]
    [InlineData("'GBP 0.01'", "yes")]
    [InlineData("'GBP -3'", "yes")]
    public void ZeroMoneyIsFalse(string value, string expected)
    {
        Assert.Equal(expected, Code($"x: money = {value}\necho $x ? 'yes' : 'no'"));
        Assert.Equal(expected == "no" ? "true" : "false", Code($"x: money = {value}\necho not $x"));
    }

    [Fact]
    public void MoneyCanNotBeWalked()
    {
        Assert.Contains("can't be walked", Fails("x: money = 'GBP 1'\nfor p in $x { echo $p }"));
        Assert.Contains("don't make ranges", Fails("a: money = 'GBP 1'\nb: money = 'GBP 5'\necho $a..$b"));
    }

    [Fact]
    public void MoneyDeclaresWhatTheEditorShows()
    {
        NativeTypeInfo info = NativeTypes.Get("money");
        Assert.Equal("value", info.Equality);
        Assert.Equal("value", info.Ordering);
        Assert.Null(info.Iterates);
        Assert.NotNull(info.Falsy);
        Assert.Contains(info.Operators, o => o.Operator == "/" && o.Left == "money" && o.Right == "money" && o.Result == "number");
        Assert.Contains(info.Operators, o => o.Operator == "*" && o.Left == "number" && o.Right == "money" && o.Result == "money");
        Assert.Contains(info.Operators, o => o.Operator == "-" && o.Left == null && o.Result == "money");
    }

    // ---- the types with no operators ------------------------------------------------------------------------------

    [Theory]
    [InlineData("phone", "'+447700900123'")]
    [InlineData("country", "'GB'")]
    [InlineData("currency", "'GBP'")]
    [InlineData("locale", "'en-GB'")]
    [InlineData("iban", "'GB82WEST12345698765432'")]
    [InlineData("card", "'4242 4242 4242 4242'")]
    public void TypesWithoutOperatorsSayWhat(string type, string value)
    {
        string name = char.ToUpperInvariant(type[0]) + type[1..];
        Assert.Contains($"{name} values have no '+'", Fails($"x: {type} = {value}\necho $x + 1"));
        Assert.Contains($"{name} values have no '*'", Fails($"x: {type} = {value}\necho $x * 2"));
        Assert.Empty(NativeTypes.Get(type).Operators);
        Assert.Null(NativeTypes.Get(type).Falsy);
        Assert.Contains("can't be walked", Fails($"x: {type} = {value}\nfor c in $x {{ echo $c }}"));
        // Every value is true: no zero.
        Assert.Equal("yes", Code($"x: {type} = {value}\necho $x ? 'yes' : 'no'"));
    }

    [Theory]
    [InlineData("country", "'gb'", "'fr'", "false true")]
    [InlineData("currency", "'GBP'", "'EUR'", "false true")]
    [InlineData("locale", "'en-GB'", "'en'", "false true")]
    public void CodesSortAlphabetically(string type, string a, string b, string expected)
    {
        Assert.Equal(expected, Code($"a: {type} = {a}\nb: {type} = {b}\necho \"{{ $a < $b }} {{ $a > $b }}\""));
    }

    [Fact]
    public void ListsOfCodesSort()
    {
        Assert.Equal("DE, FR, GB", Code("a: country = 'gb'\nb: country = 'France'\nc: country = 'DEU'\necho [$a, $b, $c]->sort()"));
        Assert.Equal("de, en, en-GB", Code("a: locale = 'en-GB'\nb: locale = 'de'\nc: locale = 'en'\necho [$a, $b, $c]->sort()"));
    }

    [Theory]
    [InlineData("phone", "'+447700900123'", "'+14155550100'")]
    [InlineData("iban", "'GB82WEST12345698765432'", "'GB33BUKB20201555555555'")]
    [InlineData("card", "'4242 4242 4242 4242'", "'5555 5555 5555 4444'")]
    public void NumbersHaveNoOrder(string type, string a, string b)
    {
        string name = char.ToUpperInvariant(type[0]) + type[1..];
        Assert.Contains($"{name} values have no order", Fails($"a: {type} = {a}\nb: {type} = {b}\necho $a < $b"));
    }

    [Theory]
    [InlineData("phone", "'+44 7700 900123'", "'+447700900123'", "'+447700900124'")]
    [InlineData("country", "'United Kingdom'", "'GB'", "'FR'")]
    [InlineData("currency", "'gbp'", "'GBP'", "'EUR'")]
    [InlineData("locale", "'en_gb'", "'en-GB'", "'en'")]
    [InlineData("iban", "'gb82 west 1234 5698 7654 32'", "'GB82WEST12345698765432'", "'GB33BUKB20201555555555'")]
    [InlineData("card", "'4242-4242-4242-4242'", "'4242424242424242'", "'5555555555554444'")]
    public void EqualityIsByTheTidiedValue(string type, string value, string same, string other)
    {
        Assert.Equal("true false", Code($"x: {type} = {value}\necho \"{{ $x == {same} }} {{ $x == {other} }}\""));
    }

    [Fact]
    public void ACardStaysHiddenWhenCompared()
    {
        // == answers true or false and nothing more; the error for < shows only the mask.
        string error = Fails("a: card = '4242 4242 4242 4242'\nb: card = '5555 5555 5555 4444'\necho $a < $b");
        Assert.DoesNotContain("4242 4242 4242 4242", error);
        Assert.DoesNotContain("424242", error);
        Assert.DoesNotContain("555555", error);
        Assert.DoesNotContain("555555", Fails("a: card = '5555 5555 5555 4444'\necho $a + 1"));
        Assert.Equal("false", Code("a: card = '4242 4242 4242 4242'\necho $a == '•••• 4242'"));
    }

    [Fact]
    public void OldMembersStillWork()
    {
        Assert.Equal("44 7700900123 tel:+447700900123", Code("p: phone = '+44 7700 900123'\necho \"{ $p.country_code } { $p.national } { $p->tel() }\""));
        Assert.Equal("Germany DEU EUR 🇩🇪 currency", Code("c: country = 'de'\necho \"{ $c.name } { $c.alpha3 } { $c.currency } { $c.flag } { $c.currency->type() }\""));
        Assert.Equal("British Pound £ 2 £1,234.50", Code("c: currency = 'GBP'\necho \"{ $c.name } { $c.symbol } { $c.digits } { $c->format(1234.5) }\""));
        Assert.Equal("de DE 1.234,5", Code("l: locale = 'de-DE'\necho \"{ $l.language } { $l.region } { $l->format(1234.5) }\""));
        Assert.Equal("GB country GB82 WEST 1234 5698 7654 32", Code("i: iban = 'GB82WEST12345698765432'\necho \"{ $i.country } { $i.country->type() } { $i->formatted() }\""));
        Assert.Equal("•••• 4444 4444 mastercard 16", Code("c: card = '5555 5555 5555 4444'\necho \"$c { $c.last4 } { $c.brand } { $c->reveal()->count() }\""));
        Assert.Contains("must be a +44 number", Fails("p: phone(countries: ['+44']) = '+14155550100'\necho $p"));
    }
}
