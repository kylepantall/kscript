using KScript.Metadata;
using KScript.Runtime;

namespace KScript.Tests;

/// <summary>
/// What url, domain, ip, cidr, mac and port declare on the NativeType contract: ip arithmetic, ranges and numeric
/// order, ip in cidr, walking a cidr block, the orders of the others, and the errors for what they don't take.
/// </summary>
public class NativeOperatorsNetworkTests
{
    private static string Code(string code) => ScriptRunner.Run(code, path: Path.Combine(Path.GetTempPath(), "natives-net-test.kscript")).TrimEnd('\n');

    private static string Fails(string code) => Assert.Throws<KScriptException>(() => Code(code)).Message;

    private const string Ips = "a: ip = '10.0.0.9'\nb: ip = '10.0.0.10'\n";

    [Theory]
    // ip arithmetic
    [InlineData(Ips + "echo $a + 1", "10.0.0.10")]
    [InlineData(Ips + "echo $a + 247", "10.0.1.0")]
    [InlineData(Ips + "echo $b - 10", "10.0.0.0")]
    [InlineData(Ips + "echo ($a + 1)->type()", "ip")]
    [InlineData(Ips + "echo $b - $a", "1")]
    [InlineData(Ips + "echo $a - $b", "-1")]
    [InlineData(Ips + "echo $b - '10.0.0.1'", "9")]
    [InlineData("x: ip = '2001:db8::ffff'\necho $x + 1", "2001:db8::1:0")]
    // ip order: numeric, not alphabetical
    [InlineData(Ips + "echo $a < $b", "true")]
    [InlineData(Ips + "echo $b > $a", "true")]
    [InlineData(Ips + "echo $a >= '10.0.0.9'", "true")]
    [InlineData(Ips + "echo [$b, $a]->sort()", "10.0.0.9, 10.0.0.10")]
    [InlineData("a: ip = '::1'\nb: ip = '255.255.255.255'\necho [$a, $b]->sort()", "255.255.255.255, ::1")]
    // ip ranges
    [InlineData(Ips + "s: ip = '10.0.0.7'\necho $s..$b", "10.0.0.7, 10.0.0.8, 10.0.0.9")]
    [InlineData(Ips + "s: ip = '10.0.0.1'\necho $s..$b step 4", "10.0.0.1, 10.0.0.5, 10.0.0.9")]
    [InlineData(Ips + "echo $b..'10.0.0.7'", "10.0.0.10, 10.0.0.9, 10.0.0.8")]
    // ip in cidr, cidr in cidr
    [InlineData(Ips + "echo $a in '10.0.0.0/8'", "true")]
    [InlineData(Ips + "c: cidr = '192.168.0.0/16'\necho $a in $c", "false")]
    [InlineData("c: cidr = '10.0.0.0/8'\necho '10.1.2.3' in $c", "true")]
    [InlineData("c: cidr = '10.1.0.0/16'\necho $c in '10.0.0.0/8'", "true")]
    [InlineData("c: cidr = '10.0.0.0/8'\necho $c in '10.1.0.0/16'", "false")]
    // cidr order: network, then prefix
    [InlineData("a: cidr = '10.0.0.0/16'\nb: cidr = '10.0.0.0/8'\nc: cidr = '9.0.0.0/8'\necho [$a, $b, $c]->sort()", "9.0.0.0/8, 10.0.0.0/8, 10.0.0.0/16")]
    [InlineData("a: cidr = '10.0.0.0/24'\necho $a < '10.0.1.0/24'", "true")]
    [InlineData("c: cidr = '10.0.0.0/8'\necho $c.last", "10.255.255.255")]
    // domain, url and mac sort by text
    [InlineData("a: domain = 'b.example'\nb: domain = 'a.example'\necho [$a, $b]->sort()", "a.example, b.example")]
    [InlineData("a: domain = 'a.example'\necho $a < 'b.example'", "true")]
    [InlineData("a: url = 'https://b.example'\nb: url = 'https://a.example'\necho [$a, $b]->sort()", "https://a.example, https://b.example")]
    [InlineData("a: mac = '00:1a:2b:3c:4d:5f'\necho $a > '00-1A-2B-3C-4D-5E'", "true")]
    // port is a number
    [InlineData("p: port = 8080\necho $p + 1", "8081")]
    [InlineData("p: port = 443\necho $p < 1024", "true")]
    // what still works as before
    [InlineData(Ips + "echo $a in ['10.0.0.9', '10.0.0.1']", "true")]
    [InlineData(Ips + "echo $a in [$b]", "false")]
    [InlineData(Ips + "echo $a == ' 10.0.0.9 '", "true")]
    [InlineData(Ips + "echo $a + '/32'", "10.0.0.9/32")]
    [InlineData("u: url = 'https://example.com'\necho $u + '/x'", "https://example.com/x")]
    [InlineData("u: url = 'https://Example.com/x'\necho $u == 'https://example.com/x'", "true")]
    [InlineData("i: ip = '10.0.0.1'\necho $i->in('10.0.0.0/8')", "true")]
    public void Works(string code, string expected) => Assert.Equal(expected, Code(code));

    [Fact]
    public void ACidrBlockWalksItsAddresses()
    {
        Assert.Equal("10.0.0.4 10.0.0.5 10.0.0.6 10.0.0.7", Code("c: cidr = '10.0.0.4/30'\nout = []\nfor x in $c {\n  out = $out + [$x]\n}\necho $out->join(' ')"));
        Assert.Equal("ip 65536", Code("c: cidr = '10.0.0.0/16'\nn = 0\nt = ''\nfor x in $c {\n  n = $n + 1\n  t = $x->type()\n}\necho \"{ $t } { $n }\""));
    }

    [Fact]
    public void AHugeBlockIsNotWalked()
    {
        string message = Fails("c: cidr = '10.0.0.0/8'\nfor x in $c {\n  echo $x\n}");
        Assert.Contains("16777216 addresses", message);
        Assert.Contains(".last", message);
    }

    [Theory]
    [InlineData(Ips + "echo $a * 2", "Ip values have no '*'")]
    [InlineData(Ips + "echo $a + 1.5", "whole number")]
    [InlineData("a: ip = '255.255.255.255'\necho $a + 1", "IPv4 addresses")]
    [InlineData("a: ip = '10.0.0.1'\nb: ip = '::1'\necho $a - $b", "aren't both IPv4 or both IPv6")]
    [InlineData(Ips + "echo $a..$b step 0", "step of 0")]
    [InlineData("u: url = 'https://example.com'\necho $u - 1", "Url values have no '-'")]
    [InlineData("d: domain = 'example.com'\necho $d * 2", "Domain values have no '*'")]
    [InlineData("m: mac = '00:1a:2b:3c:4d:5e'\necho $m + 1", "Mac values have no '+'")]
    [InlineData("c: cidr = '10.0.0.0/8'\necho $c + 1", "Cidr values have no '+'")]
    [InlineData("a: url = 'https://example.com'\nb: url = 'https://example.org'\necho $a..$b", "Url values don't make ranges")]
    public void Refuses(string code, string error) => Assert.Contains(error, Fails(code));

    [Fact]
    public void TheEditorSeesTheDeclarations()
    {
        Code("echo 1");
        NativeTypeInfo ip = NativeTypes.Get("ip");
        Assert.Equal("value", ip.Ordering);
        Assert.Contains(ip.Operators, o => o.Display == "ip + number" && o.Result == "ip");
        Assert.Contains(ip.Operators, o => o.Display == "ip - ip" && o.Result == "number");
        Assert.Contains(ip.Operators, o => o.Operator == ".." && o.Result == "ip[]");
        NativeTypeInfo cidr = NativeTypes.Get("cidr");
        Assert.Equal("ip", cidr.Iterates);
        Assert.Contains(cidr.Operators, o => o.Display == "ip in cidr" && o.Result == "bool");
        Assert.Equal("text", NativeTypes.Get("domain").Ordering);
        Assert.Equal("text", NativeTypes.Get("url").Ordering);
        Assert.Equal("text", NativeTypes.Get("mac").Ordering);
        Assert.Empty(NativeTypes.Get("url").Operators);
        Assert.Null(NativeTypes.Get("ip").Falsy);
    }
}
