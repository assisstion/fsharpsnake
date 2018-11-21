module ProjectParser

open System
open System.Text.RegularExpressions
open Parser

type BinaryOp =
// Numerical operations
| Add
| Sub
| Mult
| Div
// Comparison operations
| Eq
| Neq
| Leq
| Geq
| Lt
| Gt
let binaryOps = [Add; Sub; Mult; Div; Eq; Neq; Leq; Geq; Lt; Gt]
let precedences = [[Mult; Div]; [Add; Sub]; [Eq; Neq; Leq; Geq; Lt; Gt]]

type Expr =
| FunctionCallExpr of string * List<Expr>
| Identifier of string
| NumLiteral of int
| StringLiteral of string
| BoolLiteral of bool
| ParensExpr of Expr
| BinaryExpr of BinaryOp * Expr * Expr

type Stmt =
| FunctionCallStmt of string * List<Expr>
| AssignmentStmt of string * Expr
| LetStmt of string * Expr
| ReturnStmt of Expr
| IfElseStmt of 
    (Expr * List<Stmt>) * 
    List<Expr * List<Stmt>> * 
    Option<List<Stmt>>

type Defn =
// Function with (name, arg_names[], body)
| FunctionDefn of string * List<string> * List<Stmt>
| ScopeDefn of string * List<Defn>
| AssignmentDefn of string * Expr

type Value =
| ValNone
| ValBool of bool
| ValInt of int
| ValString of string
| ValFunc of List<string> * List<Stmt>
| ValList of List<Value>
| ValReference of string
| ValBuiltinFunc of (List<Value> -> Value)

let prettyprintfunc stringify exprs =
    "(" + (List.fold (fun a b -> 
            (if a <> "" then a + ", " else "") + stringify b
        ) "" exprs) 
        + ")"

let optostr op =
    match op with
    | Add -> "+"
    | Sub -> "-"
    | Mult -> "*"
    | Div -> "/"
    | Eq -> "=="
    | Neq -> "!="
    | Leq -> "<="
    | Geq -> ">="
    | Lt -> "<"
    | Gt -> ">"

let rec prettyprintexpr expr =
    match expr with
    | FunctionCallExpr(name, exprs) -> prettyprintcall name exprs
    | Identifier(name) -> name
    | NumLiteral(num) -> sprintf "%i" num
    | StringLiteral(str) -> sprintf "\"%s\"" str
    | BoolLiteral(b) -> if b then "true" else "false"
    | ParensExpr(e) -> sprintf "(%s)" (prettyprintexpr e)
    | BinaryExpr(op, e1, e2) -> prettyprintinfix (optostr op) e1 e2
        
and prettyprintcall name exprs =
    name + ((prettyprintfunc prettyprintexpr) exprs)
and prettyprintinfix op e1 e2 =
    sprintf "%s %s %s" 
            (prettyprintexpr e1) op (prettyprintexpr e2)

let prettyprintassignment name expr =
    name + " = " + (prettyprintexpr expr)

let prettyprintlet name expr =
    "let " + (prettyprintassignment name expr)

let indentationSize = 4
// Four space indentation only for now
let indentation = String.replicate indentationSize " "

let indent xs =
    List.map (fun s -> indentation + s) xs

let rec prettyprintgroup header innerPrinter block =
    header + ":"
            :: (indent (block |> List.collect innerPrinter))

let rec prettyprintstmt stmt =
    match stmt with
    | FunctionCallStmt(name, exprs) -> [prettyprintcall name exprs]
    | AssignmentStmt(name, expr) -> [prettyprintassignment name expr]
    | LetStmt(name, expr) -> [prettyprintlet name expr]
    | ReturnStmt(expr) -> ["return " + prettyprintexpr expr]
    | IfElseStmt((cond, block), condBlockList, optionBlock) ->
        let elseLines = 
            match optionBlock with
            | None -> []
            | Some v -> prettyprintgroup "else" prettyprintstmt v
        List.concat 
            [
                prettyprintgroup ("if " + prettyprintexpr cond) 
                    prettyprintstmt block
                List.collect (fun (innerCond, innerBlock) -> 
                    (prettyprintgroup ("elif " + prettyprintexpr innerCond)
                        prettyprintstmt innerBlock)
                    ) condBlockList
                elseLines
            ]


let prettyprintlist printer xs =
    xs
    |> List.map (printer >> (List.fold (fun a b -> a + "\n" + b) ""))
    |> List.fold (+) ""

let rec prettyprintdef def =
    match def with
    | FunctionDefn(name, args, body) -> 
        prettyprintgroup (name + (prettyprintfunc id args)) 
            prettyprintstmt body
    | ScopeDefn(name, body) -> 
        prettyprintgroup name prettyprintdef body
    | AssignmentDefn(name, expr) -> [prettyprintassignment name expr]

let prettyprint =
    prettyprintlist prettyprintdef

let poption (parser: Parser<'a>) input : Outcome<'a Option> =
    match parser input with
    | Success (res, rem) -> Success (Some res, rem)
    | Failure -> Success (None, input)

//parsers should not be empty
//let pinfix (opparser: Parser<'a>) (leftparser: Parser<'b>) 
//    (rightparser: Parser<'c>) (combiner: 'b -> 'c -> 'd) : Parser<'d> =
    

let isSymb c = is_regexp (c.ToString()) @"[_$]"
let isLetterSymb c = is_letter c || isSymb c
let isValidChar c = isLetterSymb c || is_digit c
let isNotQuote c = c <> '\"'

let psymb = psat isSymb
let plettersymb = psat isLetterSymb
let pvalidchar = psat isValidChar
let charListToString str = 
    str |> List.map (fun x -> x.ToString()) |> String.concat ""
let pidentifier = 
    pseq plettersymb (pmany0 pvalidchar) 
        (fun (x,xs) -> 
            (x :: xs) 
            |> charListToString
        )

let pidsep = (poption (pchar '|'))
let pidExpr = pidentifier |>> Identifier

let plistsep sepParser innerParser =
    poption (pseq (pmany0 (pleft innerParser sepParser)) innerParser 
        (fun (xs, x) -> List.append xs [x])) |>>
        (fun x ->
            match x with
            | Some y -> y
            | None -> List.empty
        )

let pFuncHelper innerParser = 
    pseq (pleft pidentifier (pchar '('))
        (pleft 
            (plistsep (pchar ',') innerParser) 
            (pchar ')')
        )
        (fun (name, args) -> (name, args))


let (pExpr : Parser<Expr>), pExprImpl = recparser()

let pFuncCall = pFuncHelper pExpr

let pFuncCallExpr = pFuncCall |>> FunctionCallExpr
let pFuncCallStmt = pFuncCall |>> FunctionCallStmt

let pNumLiteral = 
    pseq (poption (pchar '-')) 
        (pmany1 pdigit |>> charListToString |>> int)
        (fun (oc, num) ->
            match oc with
            | Some _ -> -num
            | None -> num
        ) |>> NumLiteral

//String parsing is handled by the upper parser

//let pStrInner = (pright (pchar '\\') pitem) <|> (psat isNotQuote)
//let pStrLiteral = 
//    pleft (pright (pchar '"') (pmany0 pStrInner)) (pchar '"') 
//        |>> charListToString 
//        |>> StringLiteral
let pTrueLiteral = pfresult (pstr "true") (BoolLiteral true)
let pFalseLiteral = pfresult (pstr "false") (BoolLiteral false)
let pBoolLiteral = pTrueLiteral <|> pFalseLiteral

let pParens = pbetween (pchar '(') (pchar ')') pExpr |>> ParensExpr

let pConsumingExpr = 
    pFuncCallExpr
    <|> pBoolLiteral
    <|> pidExpr
    <|> pNumLiteral
//    <|> pStrLiteral
    <|> pParens
let makebin bop (a, b) =
    BinaryExpr (bop, a, b)

let pbinop op = 
    let str = (optostr op)
    let exprgen = makebin op
    pfresult (pstr str) (op, exprgen)

let pinfixop =
    List.fold (fun parser op -> (parser <|> (pbinop op))) pzero binaryOps

let rec combineSinglePrecOp right cexpr xs predicate =
    match xs with
    | [] -> cexpr, []
    | ((name, op), term) :: remaining ->
        let expr, newRemaining = 
            combineSinglePrecOp right term remaining predicate
        if predicate name then
            let opin = if right then (cexpr, expr) else (expr, cexpr)
            (op opin), newRemaining
        else
            cexpr, ((name, op), expr) :: newRemaining

let rec rightToLeftAssoc cexpr xs ys =
    match xs with
    | [] -> cexpr, ys
    | (opn, expr) :: remaining -> 
        rightToLeftAssoc expr remaining ((opn, cexpr) :: ys)

let rec combineOps right precs (cexpr, xs) =
    match precs with
    | [] -> 
        let newExpr, _ = combineSinglePrecOp right cexpr xs (fun x -> true)
        newExpr
    | singlePrec :: remaining ->
        let newExpr, nxs = 
            combineSinglePrecOp right cexpr xs 
                (fun x -> List.contains x singlePrec)
        combineOps right remaining (newExpr, nxs)

let combineOpsRight precs (cexpr, xs) = 
    combineOps true precs (cexpr, xs)

let combineOpsLeft precs (cexpr, xs) =
    combineOps false precs (rightToLeftAssoc cexpr xs [])

pExprImpl := 
    pseq pConsumingExpr 
        (pmany0 
            (pseq pinfixop 
                pConsumingExpr
                id
            )
        )
        (combineOpsLeft precedences)
    

let pAssignment = 
    pseq (pleft pidentifier (pchar '=')) pExpr 
        (fun (name, expr) -> (name, expr))
let pAssignmentStmt = pAssignment |>> AssignmentStmt
let pAssignmentGlobal = pAssignment |>> AssignmentDefn


let pIdAssign = 
    (pseq (pleft pidentifier pidsep) pAssignment)
        (fun (a, b) ->
            (a, b)
        )

let pIdExpr = 
    (pseq (pleft pidentifier pidsep) pExpr) 
        (fun (a, b) ->
            (a, b)
        )

let pWordStmt pidx str outputProcessor input =
    let outcome = pidx input
    match outcome with
    | Success ((iden, parsed), remaining) ->
        if iden = str then
            Success (outputProcessor parsed, remaining)
        else
            Failure
    | Failure -> Failure

let pWordAssignStmt = pWordStmt pIdAssign "let" LetStmt

let pWordExprStmt = pWordStmt pIdExpr "return" ReturnStmt

let pStmt = 
    pWordExprStmt
    <|> pWordAssignStmt
    <|> pFuncCallStmt
    <|> pAssignmentStmt

let pLine parser = pbetween (pstr "{l|") (pstr "|l}") parser
let pBlock parser = pbetween (pstr "{b|") (pstr "|b}") (pmany1 parser)
let pFuncHeader = pFuncHelper pidentifier

let pGroup header blockElem converter =
    pbetween (pstr "{s|") (pstr "|s}") (
        pseq header (pBlock blockElem)
            converter
    )

let pIfHeader = pWordStmt pIdExpr "if" id
let pElifHeader = pWordStmt pIdExpr "elif" id
let pElseHeader = pWordStmt (pidentifier |>> (fun a -> a, ())) "else" id

//IfElseStmt((name, block), List.empty, None))

let rec pFuncScope header =
    let b : Parser<Stmt> = ((pLine pStmt) <|> pInnerScope)
    pGroup header b
and pIfGroup a =
    a |> 
        (pseq 
            (pseq (pFuncScope pIfHeader id)
                (pmany0 (pFuncScope pElifHeader id))
                id
            ) (poption (pFuncScope pElseHeader (fun (_, stmts) -> stmts)))
            (fun (((cond, block), condBlockList), optionBlock) -> 
                IfElseStmt((cond, block), condBlockList, optionBlock))
        )
and pInnerScope = pIfGroup

let (pDefn : Parser<Defn>), pDefnImpl = recparser()

let pScope =
    pGroup pidentifier pDefn
        (fun (name, defns) -> ScopeDefn(name, defns))

let pFunc =
    pFuncScope pFuncHeader 
        (fun ((name, args), block) -> FunctionDefn(name, args, block))

pDefnImpl := pFunc <|> pScope <|> (pLine pAssignmentGlobal)
let pProgram = pmany1 pDefn

let grammar = pleft pProgram peof

let parseLower input : List<Defn> option =
    match grammar (prepare input) with
    | Success(e,_) -> Some e
    | Failure -> None

let cleanLower input =
    let clean0 = Regex.Replace(input, @"[\n\r]+", "")
    let clean1 = Regex.Replace(clean0, @"[\s]{2,}", " ")
    let clean2 = 
        Regex.Replace(clean1, @"([A-Za-z0-9_]) ([A-Za-z0-9_])", "$1|$2")
    let clean3 = Regex.Replace(clean2, @" ", "")
    clean3


type Block =
| NoBlock
| Line of string
| SubBlock of string * List<Block>

let rec prettyprintblock block =
    match block with
    | NoBlock -> []
    | Line l -> [l]
    | SubBlock(title, sub) ->
        title + ":" :: (indent (sub |> List.collect prettyprintblock))

let rec findIndented (stringList : List<string>) =
    match stringList with
    | [] -> [], []
    | line :: remaining ->
        if line.Substring(0, indentationSize) = indentation then
            let unindented = line.Substring(indentationSize)
            let remainingIndented, rest = findIndented remaining
            unindented :: remainingIndented, rest
        else
            [], stringList

let rec parseUpper stringList =
    match stringList with
    | [] -> []
    | line :: remaining ->
        let cleanLine = Regex.Replace(line, @":\s+$", ":")
        if cleanLine.Chars(cleanLine.Length - 1) = ':' then
            let cleanLine2 = cleanLine.Substring(0, cleanLine.Length - 1)
            let indented, rest = findIndented remaining
            (SubBlock(cleanLine2, parseUpper indented)) :: (parseUpper rest)
        else
            Line line :: (parseUpper remaining)

let stringExtract i = 
    sprintf "$%i$" i

let stringReplace (str : string) index length replacement =
    String.concat "" [str.[0 .. index - 1]; 
                        replacement; 
                        str.[index + length .. str.Length - 1]]

let extractQuotes line (strings : string[]) =
    let m = Regex.Match(line, "(\"([^\"\\\\]|\\\\\\\\|\\\\\")+\")")
    if m.Success then
        let firstMatch = m.Groups.[1]
        let text = firstMatch.Value
        let text2 = Regex.Replace(text, "\\\\\\\\", "\\\\")
        let text3 = Regex.Replace(text2, "\\\\\"", "\"")
        //printfn "L:%s;fmv:%s;fmi:%i;fml:%i" line text 
        //    firstMatch.Index firstMatch.Length
        let outLine = 
            stringReplace line firstMatch.Index firstMatch.Length 
                (stringExtract strings.Length)
        //Text contains quotes, so remove first and last char
        outLine, Array.append strings [|text3.[1 .. text3.Length - 2]|]
    else
        line, strings

let cleanUpperLine line strings =
    //Extract string literals - do this before other cleanup
    let newLine, newStrings = extractQuotes line strings
    //Replace illegal chars
    let clean0 = Regex.Replace(newLine, @"[\{\|\}]", "")
    //Replace comments
    let clean1 = Regex.Replace(clean0, @"\#.+", "")
    if Regex.Replace(clean1, @"\s", "") = "" then
        "", newStrings
    else
        clean1, newStrings

let cleanUpper stringList =
    let cleanUpperInner str (xs, assigns) =
        let cleaned, newAssigns = cleanUpperLine str assigns
        cleaned :: xs, newAssigns
    let newLines, strings = List.foldBack cleanUpperInner stringList ([], [||])
    let newLines2 = List.filter (fun x -> x <> "") newLines
    newLines2, strings

let rec upperToLower blockList =
    let upperToLowerInner state block =
        match state with
        | None -> None
        | Some s ->
            match upperToLowerSingle block with
            | None -> None
            | Some s2 -> Some (s + s2)
    List.fold upperToLowerInner (Some "") blockList
and upperToLowerSingle block =
    match block with
    | NoBlock -> Some ""
    | Line line -> 
        //Make sure line does not start with spaces
        if line.StartsWith " " then
            None 
        else
            Some ("{l|" + line + "|l}")
    | SubBlock(title, sub) -> 
        if title.StartsWith " " then
            None
        else
            match upperToLower sub with
            | None -> None
            | Some v -> Some ("{s|" + title + "{b|" + v + "|b}|s}")

let assignStrings assigns =
    let xs = Array.toList assigns
    let xsz = List.zip [0..(xs.Length - 1)] xs
    List.map (fun (index, elem) -> 
        AssignmentDefn(stringExtract index, StringLiteral(elem))) xsz

let parseComplete lines =
    let cleanLines, assigns = cleanUpper lines
    let parsed = parseUpper cleanLines
    match upperToLower parsed with
    | None -> None
    | Some lower ->
        let cleaned = cleanLower lower
        match parseLower cleaned with
        | None -> None
        | Some s -> Some (List.append (assignStrings assigns) s)
        