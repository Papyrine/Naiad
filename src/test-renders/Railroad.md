# Railroad

## Constructors

**Input:**
```
railroad-beta
title Expression Grammar

expression = sequence(
    nonterminal("term"),
    zeroOrMore(sequence(
        choice(terminal("+"), terminal("-")),
        nonterminal("term")
    ))
) ;
term = sequence(
    nonterminal("factor"),
    zeroOrMore(sequence(
        choice(terminal("*"), terminal("/")),
        nonterminal("factor")
    ))
) ;
factor = choice(
    nonterminal("number"),
    sequence(terminal("("), nonterminal("expression"), terminal(")"))
) ;
number = oneOrMore(nonterminal("digit")) ;
digit = choice(terminal("0"), terminal("1"), terminal("2"), terminal("3"), terminal("4"), terminal("5"), terminal("6"), terminal("7"), terminal("8"), terminal("9")) ;
```
**Rendered by Naiad:**

<p align="center">
  <img src="../Tests/Railroad/RailroadTests.Constructors.verified.png" />
</p>


[Open in Mermaid Live](https://mermaid.live/edit#base64:eyJjb2RlIjoicmFpbHJvYWQtYmV0YVxudGl0bGUgRXhwcmVzc2lvbiBHcmFtbWFyXG5cbmV4cHJlc3Npb24gPSBzZXF1ZW5jZShcbiAgICBub250ZXJtaW5hbChcdTAwMjJ0ZXJtXHUwMDIyKSxcbiAgICB6ZXJvT3JNb3JlKHNlcXVlbmNlKFxuICAgICAgICBjaG9pY2UodGVybWluYWwoXHUwMDIyXHUwMDJCXHUwMDIyKSwgdGVybWluYWwoXHUwMDIyLVx1MDAyMikpLFxuICAgICAgICBub250ZXJtaW5hbChcdTAwMjJ0ZXJtXHUwMDIyKVxuICAgICkpXG4pIDtcbnRlcm0gPSBzZXF1ZW5jZShcbiAgICBub250ZXJtaW5hbChcdTAwMjJmYWN0b3JcdTAwMjIpLFxuICAgIHplcm9Pck1vcmUoc2VxdWVuY2UoXG4gICAgICAgIGNob2ljZSh0ZXJtaW5hbChcdTAwMjIqXHUwMDIyKSwgdGVybWluYWwoXHUwMDIyL1x1MDAyMikpLFxuICAgICAgICBub250ZXJtaW5hbChcdTAwMjJmYWN0b3JcdTAwMjIpXG4gICAgKSlcbikgO1xuZmFjdG9yID0gY2hvaWNlKFxuICAgIG5vbnRlcm1pbmFsKFx1MDAyMm51bWJlclx1MDAyMiksXG4gICAgc2VxdWVuY2UodGVybWluYWwoXHUwMDIyKFx1MDAyMiksIG5vbnRlcm1pbmFsKFx1MDAyMmV4cHJlc3Npb25cdTAwMjIpLCB0ZXJtaW5hbChcdTAwMjIpXHUwMDIyKSlcbikgO1xubnVtYmVyID0gb25lT3JNb3JlKG5vbnRlcm1pbmFsKFx1MDAyMmRpZ2l0XHUwMDIyKSkgO1xuZGlnaXQgPSBjaG9pY2UodGVybWluYWwoXHUwMDIyMFx1MDAyMiksIHRlcm1pbmFsKFx1MDAyMjFcdTAwMjIpLCB0ZXJtaW5hbChcdTAwMjIyXHUwMDIyKSwgdGVybWluYWwoXHUwMDIyM1x1MDAyMiksIHRlcm1pbmFsKFx1MDAyMjRcdTAwMjIpLCB0ZXJtaW5hbChcdTAwMjI1XHUwMDIyKSwgdGVybWluYWwoXHUwMDIyNlx1MDAyMiksIHRlcm1pbmFsKFx1MDAyMjdcdTAwMjIpLCB0ZXJtaW5hbChcdTAwMjI4XHUwMDIyKSwgdGVybWluYWwoXHUwMDIyOVx1MDAyMikpIDsiLCJtZXJtYWlkIjp7InRoZW1lIjoiZGVmYXVsdCJ9fQ==)

## ConstructorsOptionalAndSpecial

**Input:**
```
railroad-beta
%% a comment
statement = sequence(
    optional(terminal("export")),
    choice(nonterminal("declaration"), special("any expression")),
    optional(sequence(terminal(";"), optional(nonterminal("trailing_comment"))))
) ; /* trailing */
```
**Rendered by Naiad:**

<p align="center">
  <img src="../Tests/Railroad/RailroadTests.ConstructorsOptionalAndSpecial.verified.png" />
</p>


[Open in Mermaid Live](https://mermaid.live/edit#base64:eyJjb2RlIjoicmFpbHJvYWQtYmV0YVxuJSUgYSBjb21tZW50XG5zdGF0ZW1lbnQgPSBzZXF1ZW5jZShcbiAgICBvcHRpb25hbCh0ZXJtaW5hbChcdTAwMjJleHBvcnRcdTAwMjIpKSxcbiAgICBjaG9pY2Uobm9udGVybWluYWwoXHUwMDIyZGVjbGFyYXRpb25cdTAwMjIpLCBzcGVjaWFsKFx1MDAyMmFueSBleHByZXNzaW9uXHUwMDIyKSksXG4gICAgb3B0aW9uYWwoc2VxdWVuY2UodGVybWluYWwoXHUwMDIyO1x1MDAyMiksIG9wdGlvbmFsKG5vbnRlcm1pbmFsKFx1MDAyMnRyYWlsaW5nX2NvbW1lbnRcdTAwMjIpKSkpXG4pIDsgLyogdHJhaWxpbmcgKi8iLCJtZXJtYWlkIjp7InRoZW1lIjoiZGVmYXVsdCJ9fQ==)

## Ebnf

**Input:**
```
railroad-ebnf-beta
title "Arithmetic Expression Grammar"

expression = term ( ( "+" | "-" ) term )* ;
term = factor ( ( "*" | "/" ) factor )* ;
factor = number | "(" expression ")" ;
number = digit+ ;
digit = "0" | "1" | "2" | "3" | "4" | "5" | "6" | "7" | "8" | "9" ;
```
**Rendered by Naiad:**

<p align="center">
  <img src="../Tests/Railroad/RailroadTests.Ebnf.verified.png" />
</p>


[Open in Mermaid Live](https://mermaid.live/edit#base64:eyJjb2RlIjoicmFpbHJvYWQtZWJuZi1iZXRhXG50aXRsZSBcdTAwMjJBcml0aG1ldGljIEV4cHJlc3Npb24gR3JhbW1hclx1MDAyMlxuXG5leHByZXNzaW9uID0gdGVybSAoICggXHUwMDIyXHUwMDJCXHUwMDIyIHwgXHUwMDIyLVx1MDAyMiApIHRlcm0gKSogO1xudGVybSA9IGZhY3RvciAoICggXHUwMDIyKlx1MDAyMiB8IFx1MDAyMi9cdTAwMjIgKSBmYWN0b3IgKSogO1xuZmFjdG9yID0gbnVtYmVyIHwgXHUwMDIyKFx1MDAyMiBleHByZXNzaW9uIFx1MDAyMilcdTAwMjIgO1xubnVtYmVyID0gZGlnaXRcdTAwMkIgO1xuZGlnaXQgPSBcdTAwMjIwXHUwMDIyIHwgXHUwMDIyMVx1MDAyMiB8IFx1MDAyMjJcdTAwMjIgfCBcdTAwMjIzXHUwMDIyIHwgXHUwMDIyNFx1MDAyMiB8IFx1MDAyMjVcdTAwMjIgfCBcdTAwMjI2XHUwMDIyIHwgXHUwMDIyN1x1MDAyMiB8IFx1MDAyMjhcdTAwMjIgfCBcdTAwMjI5XHUwMDIyIDsiLCJtZXJtYWlkIjp7InRoZW1lIjoiZGVmYXVsdCJ9fQ==)

## EbnfJson

**Input:**
```
railroad-ebnf-beta
title "JSON Grammar"

json = element ;
element = object | array | string | number | "true" | "false" | "null" ;
object = "{" [ member ( "," member )* ] "}" ;
array = "[" [ element ( "," element )* ] "]" ;
member = string ":" element ;
```
**Rendered by Naiad:**

<p align="center">
  <img src="../Tests/Railroad/RailroadTests.EbnfJson.verified.png" />
</p>


[Open in Mermaid Live](https://mermaid.live/edit#base64:eyJjb2RlIjoicmFpbHJvYWQtZWJuZi1iZXRhXG50aXRsZSBcdTAwMjJKU09OIEdyYW1tYXJcdTAwMjJcblxuanNvbiA9IGVsZW1lbnQgO1xuZWxlbWVudCA9IG9iamVjdCB8IGFycmF5IHwgc3RyaW5nIHwgbnVtYmVyIHwgXHUwMDIydHJ1ZVx1MDAyMiB8IFx1MDAyMmZhbHNlXHUwMDIyIHwgXHUwMDIybnVsbFx1MDAyMiA7XG5vYmplY3QgPSBcdTAwMjJ7XHUwMDIyIFsgbWVtYmVyICggXHUwMDIyLFx1MDAyMiBtZW1iZXIgKSogXSBcdTAwMjJ9XHUwMDIyIDtcbmFycmF5ID0gXHUwMDIyW1x1MDAyMiBbIGVsZW1lbnQgKCBcdTAwMjIsXHUwMDIyIGVsZW1lbnQgKSogXSBcdTAwMjJdXHUwMDIyIDtcbm1lbWJlciA9IHN0cmluZyBcdTAwMjI6XHUwMDIyIGVsZW1lbnQgOyIsIm1lcm1haWQiOnsidGhlbWUiOiJkZWZhdWx0In19)

## EbnfIsoStyle

**Input:**
```
railroad-ebnf-beta
(* ISO 14977 style, with commas between items *)
a_rather_long_rule_name_for_layout ::= [ sign ] , { digit } , ? any visible character ? ;
identifier = letter ( letter | digit )* - keyword ;
sign = "+" | "-" ;
```
**Rendered by Naiad:**

<p align="center">
  <img src="../Tests/Railroad/RailroadTests.EbnfIsoStyle.verified.png" />
</p>


[Open in Mermaid Live](https://mermaid.live/edit#base64:eyJjb2RlIjoicmFpbHJvYWQtZWJuZi1iZXRhXG4oKiBJU08gMTQ5Nzcgc3R5bGUsIHdpdGggY29tbWFzIGJldHdlZW4gaXRlbXMgKilcbmFfcmF0aGVyX2xvbmdfcnVsZV9uYW1lX2Zvcl9sYXlvdXQgOjo9IFsgc2lnbiBdICwgeyBkaWdpdCB9ICwgPyBhbnkgdmlzaWJsZSBjaGFyYWN0ZXIgPyA7XG5pZGVudGlmaWVyID0gbGV0dGVyICggbGV0dGVyIHwgZGlnaXQgKSogLSBrZXl3b3JkIDtcbnNpZ24gPSBcdTAwMjJcdTAwMkJcdTAwMjIgfCBcdTAwMjItXHUwMDIyIDsiLCJtZXJtYWlkIjp7InRoZW1lIjoiZGVmYXVsdCJ9fQ==)

## Abnf

**Input:**
```
railroad-abnf-beta
title "Email Address"

address = local-part "@" domain ;
local-part = 1*( ALPHA / DIGIT / "." / "-" ) ;
domain = label *( "." label ) ;
label = 1*( ALPHA / DIGIT / "-" ) ;
```
**Rendered by Naiad:**

<p align="center">
  <img src="../Tests/Railroad/RailroadTests.Abnf.verified.png" />
</p>


[Open in Mermaid Live](https://mermaid.live/edit#base64:eyJjb2RlIjoicmFpbHJvYWQtYWJuZi1iZXRhXG50aXRsZSBcdTAwMjJFbWFpbCBBZGRyZXNzXHUwMDIyXG5cbmFkZHJlc3MgPSBsb2NhbC1wYXJ0IFx1MDAyMkBcdTAwMjIgZG9tYWluIDtcbmxvY2FsLXBhcnQgPSAxKiggQUxQSEEgLyBESUdJVCAvIFx1MDAyMi5cdTAwMjIgLyBcdTAwMjItXHUwMDIyICkgO1xuZG9tYWluID0gbGFiZWwgKiggXHUwMDIyLlx1MDAyMiBsYWJlbCApIDtcbmxhYmVsID0gMSooIEFMUEhBIC8gRElHSVQgLyBcdTAwMjItXHUwMDIyICkgOyIsIm1lcm1haWQiOnsidGhlbWUiOiJkZWZhdWx0In19)

## AbnfBoundedRepetition

**Input:**
```
railroad-abnf-beta
; Counted repetitions and numeric values
phone = [ "+" country-code ] subscriber ;
country-code = 1*3DIGIT ;
subscriber = 3DIGIT "-" 4DIGIT ;
hex = %x30-39 / %x41-46 ;
year = 2*4DIGIT / *2%d48 ;
```
**Rendered by Naiad:**

<p align="center">
  <img src="../Tests/Railroad/RailroadTests.AbnfBoundedRepetition.verified.png" />
</p>


[Open in Mermaid Live](https://mermaid.live/edit#base64:eyJjb2RlIjoicmFpbHJvYWQtYWJuZi1iZXRhXG47IENvdW50ZWQgcmVwZXRpdGlvbnMgYW5kIG51bWVyaWMgdmFsdWVzXG5waG9uZSA9IFsgXHUwMDIyXHUwMDJCXHUwMDIyIGNvdW50cnktY29kZSBdIHN1YnNjcmliZXIgO1xuY291bnRyeS1jb2RlID0gMSozRElHSVQgO1xuc3Vic2NyaWJlciA9IDNESUdJVCBcdTAwMjItXHUwMDIyIDRESUdJVCA7XG5oZXggPSAleDMwLTM5IC8gJXg0MS00NiA7XG55ZWFyID0gMio0RElHSVQgLyAqMiVkNDggOyIsIm1lcm1haWQiOnsidGhlbWUiOiJkZWZhdWx0In19)

## Peg

**Input:**
```
railroad-peg-beta
title "Calculator Grammar"

Expression <- Term (("+" / "-") Term)* ;
Term <- Factor (("*" / "/") Factor)* ;
Factor <- Number / "(" Expression ")" ;
Number <- Digit+ ;
Digit <- "0" / "1" / "2" / "3" / "4" / "5" / "6" / "7" / "8" / "9" ;
```
**Rendered by Naiad:**

<p align="center">
  <img src="../Tests/Railroad/RailroadTests.Peg.verified.png" />
</p>


[Open in Mermaid Live](https://mermaid.live/edit#base64:eyJjb2RlIjoicmFpbHJvYWQtcGVnLWJldGFcbnRpdGxlIFx1MDAyMkNhbGN1bGF0b3IgR3JhbW1hclx1MDAyMlxuXG5FeHByZXNzaW9uIFx1MDAzQy0gVGVybSAoKFx1MDAyMlx1MDAyQlx1MDAyMiAvIFx1MDAyMi1cdTAwMjIpIFRlcm0pKiA7XG5UZXJtIFx1MDAzQy0gRmFjdG9yICgoXHUwMDIyKlx1MDAyMiAvIFx1MDAyMi9cdTAwMjIpIEZhY3RvcikqIDtcbkZhY3RvciBcdTAwM0MtIE51bWJlciAvIFx1MDAyMihcdTAwMjIgRXhwcmVzc2lvbiBcdTAwMjIpXHUwMDIyIDtcbk51bWJlciBcdTAwM0MtIERpZ2l0XHUwMDJCIDtcbkRpZ2l0IFx1MDAzQy0gXHUwMDIyMFx1MDAyMiAvIFx1MDAyMjFcdTAwMjIgLyBcdTAwMjIyXHUwMDIyIC8gXHUwMDIyM1x1MDAyMiAvIFx1MDAyMjRcdTAwMjIgLyBcdTAwMjI1XHUwMDIyIC8gXHUwMDIyNlx1MDAyMiAvIFx1MDAyMjdcdTAwMjIgLyBcdTAwMjI4XHUwMDIyIC8gXHUwMDIyOVx1MDAyMiA7IiwibWVybWFpZCI6eyJ0aGVtZSI6ImRlZmF1bHQifX0=)

## PegPredicates

**Input:**
```
railroad-peg-beta
title "Identifiers (keywords excluded)"

Identifier <- !Keyword Letter Letter* ;  # a comment
Keyword <- "if" / "else" / "while" ;
Letter <- "a" / "b" / "c" / "_" ;
Comment <- "/*" (!"*/" .)* "*/" ;
```
**Rendered by Naiad:**

<p align="center">
  <img src="../Tests/Railroad/RailroadTests.PegPredicates.verified.png" />
</p>


[Open in Mermaid Live](https://mermaid.live/edit#base64:eyJjb2RlIjoicmFpbHJvYWQtcGVnLWJldGFcbnRpdGxlIFx1MDAyMklkZW50aWZpZXJzIChrZXl3b3JkcyBleGNsdWRlZClcdTAwMjJcblxuSWRlbnRpZmllciBcdTAwM0MtICFLZXl3b3JkIExldHRlciBMZXR0ZXIqIDsgICMgYSBjb21tZW50XG5LZXl3b3JkIFx1MDAzQy0gXHUwMDIyaWZcdTAwMjIgLyBcdTAwMjJlbHNlXHUwMDIyIC8gXHUwMDIyd2hpbGVcdTAwMjIgO1xuTGV0dGVyIFx1MDAzQy0gXHUwMDIyYVx1MDAyMiAvIFx1MDAyMmJcdTAwMjIgLyBcdTAwMjJjXHUwMDIyIC8gXHUwMDIyX1x1MDAyMiA7XG5Db21tZW50IFx1MDAzQy0gXHUwMDIyLypcdTAwMjIgKCFcdTAwMjIqL1x1MDAyMiAuKSogXHUwMDIyKi9cdTAwMjIgOyIsIm1lcm1haWQiOnsidGhlbWUiOiJkZWZhdWx0In19)

## NoRules

**Input:**
```
railroad-ebnf-beta
title "Nothing yet"
```
**Rendered by Naiad:**

<p align="center">
  <img src="../Tests/Railroad/RailroadTests.NoRules.verified.png" />
</p>


[Open in Mermaid Live](https://mermaid.live/edit#base64:eyJjb2RlIjoicmFpbHJvYWQtZWJuZi1iZXRhXG50aXRsZSBcdTAwMjJOb3RoaW5nIHlldFx1MDAyMiIsIm1lcm1haWQiOnsidGhlbWUiOiJkZWZhdWx0In19)

