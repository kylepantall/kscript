# Integer math

Calculate greatest common divisors, least common multiples and factorials with KScript Core.

```sh
kscript lib add integer-math
```

```kscript
import "lib/integer-math/index.kscript" as integers

echo @integers.gcd(-84, 30)      // 6
echo @integers.lcm(-84, 30)      // 420
echo @integers.factorial(5)      // 120
```

The parameters use KScript's `int` conversion, which truncates fractions toward zero. After conversion, `gcd(a, b)` and `lcm(a, b)` accept integers from −9,007,199,254,740,991 to 9,007,199,254,740,991. Both return a nonnegative result. Two zeroes have GCD zero, and either operand being zero gives LCM zero. An LCM above the safe integer limit raises `Argument`.

`factorial(n)` accepts 0 through 170 inclusive. Both 0! and 1! are 1. Large factorials are rounded to the precision of a KScript number.
