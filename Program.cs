using System.Text;
using System.Text.RegularExpressions;

Repl.Start();

static class Repl
{
    private static Func<State, State> Run = (State state) =>
    {
        while (true)
        {
            var next = state.Evaluate();
            if (state == next) break;
            Console.WriteLine($"  {next}");
            state = next;
        }
        return state;
    };

    public static void Start()
    {
        Console.InputEncoding = Encoding.Unicode;
        Console.OutputEncoding = Encoding.Unicode;
        Console.Write("> ");
        var input = Console.ReadLine() ?? "";
        while (input.Trim().Any())
        {
            try
            {
                var s = Parser.ParseState(input);
                Run(s);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error: {e.Message}");
            }

            Console.WriteLine("");
            Console.Write("> ");
            input = Console.ReadLine() ?? "";
        }
    }
}

static class Parser
{
    public static State ParseState(string input)
    {
        List<Expression> Tokens = new();
        int depth = 0;
        var builder = new StringBuilder();

        for (var i = 0; i < input.Length; i++)
        {
            switch (input[i])
            {
                case '(':
                    depth++;
                    builder.Append(input[i]);
                    break;
                case ')':
                    depth--;
                    builder.Append(input[i]);
                    break;
                case ' ':
                    if (depth == 0)
                    {
                        Tokens.Add(Parse(builder.ToString()));
                        builder.Clear();
                    }
                    else
                        builder.Append(input[i]);
                    break;
                default:
                    builder.Append(input[i]);
                    break;
            }
        }

        Tokens.Add(Parse(builder.ToString()));

        if (!Tokens.Any())
            throw new ArgumentException();

        return new State(
            Tokens.First(),
            Tokens.Skip(1).ToArray()
        );
    }

    static (bool Success, Application? Application) ParseApplication(string input)
    {
        var applicationRegex = new Regex(@"^\(([^ ]*) (.*)\)$");
        var applicationMatch = applicationRegex.Match(input);

        if (applicationMatch.Success)
        {
            var abstraction = applicationMatch.Groups[1].Value;
            var value = applicationMatch.Groups[2].Value;

            return (
                true,
                new Application(
                    (Abstraction)Parse(abstraction),
                    Parse(value)));
        }

        return (false, null);
    }

    static (bool Success, Abstraction? Abstraction) ParseAbstraction(string input)
    {
        var abstractionRegex = new Regex(@"^[λ\\]([a-zA-Z]+)\.(.*)$");
        var abstractionMatch = abstractionRegex.Match(input);

        if (abstractionMatch.Success)
        {
            var symbol = abstractionMatch.Groups[1].Value;
            var replacement = abstractionMatch.Groups[2].Value;

            return (
                true,
                new Abstraction(
                    symbol,
                    Parse(replacement)));
        }

        return (false, null);
    }

    static (bool Success, Symbol? Symbol) ParseSymbol(string input) => (true, new Symbol(input));

    private delegate (bool, Expression?) parser(string input);
    public static Expression Parse(string input)
    {
        List<parser> parsers = new();
        parsers.AddRange(new parser[] {
            s => ((bool, Expression?)) ParseApplication(s),
            s => ((bool, Expression?)) ParseAbstraction(s),
            s => ((bool, Expression?)) ParseSymbol(s),
        });

        foreach (var parser in parsers)
        {
            (var success, Expression? expression) = parser(input);
            if (success) return expression!;
        }

        throw new ArgumentException();
    }
}

record State
{
    public Expression Left;
    public Queue<Expression> Right = new();

    public State(Expression left, Queue<Expression> right)
    {
        Left = left;
        Right = right;
    }
    public State(Expression left, params Expression[] right)
        : this(left, new Queue<Expression>(right)) { }

    public State Evaluate() =>
        Left switch
        {
            Abstraction left =>
                Right.Any()
                    ? new State(
                        new Application(left, (new Queue<Expression>(Right)).Dequeue()),
                        new Queue<Expression>(Right.Skip(1)))
                    : new State(left, Right)
            ,
            Application left => new State(left.Evaluate(new Scope()).expression, Right),
            Symbol left => this,
            _ => throw new Exception("Incorrect repl state, dying.")
        };

    public override string ToString() => $"{Left} {string.Join(" ", Right)}";
}

abstract class Expression
{
    public abstract (Scope scope, Expression expression) Evaluate(Scope scope);

    public static implicit operator Expression(string identifier) => new Symbol(identifier);
    public static implicit operator Expression((Symbol left, Expression right) abstraction) =>
        new Abstraction(abstraction.left, abstraction.right);
    public static implicit operator Expression((Abstraction left, string right) application) =>
        new Application(application.left, application.right);

    public abstract Expression Beta(Symbol s, Expression e);
}

class Symbol : Expression
{
    public string Identifier;

    public Symbol(string identifier)
    {
        Identifier = identifier;
    }

    public override (Scope, Expression) Evaluate(Scope scope) =>
        scope.Get(this) switch
        {
            null => (scope, this),
            Expression e => (scope, e)
        };

    public static implicit operator Symbol(string identifier) => new Symbol(identifier);
    public override int GetHashCode() => Identifier.GetHashCode();
    public override bool Equals(object? obj) => (obj is Symbol s) && Identifier.Equals(s.Identifier);
    public override string ToString() => $"{Identifier}";
}

class Abstraction : Expression
{
    public Symbol Symbol;
    public Expression Replacement;

    public Abstraction(Symbol symbol, Expression replacement)
    {
        Symbol = symbol;
        Replacement = replacement;
    }

    public Expression Apply(Scope outer, Expression expression) =>
        Replacement
            .Evaluate(Scope.inner(outer, Symbol, expression))
            .expression;

    public override (Scope scope, Expression expression) Evaluate(Scope outer) =>
        (outer.Get(Symbol), Replacement) switch
        {
            (null, _) => (outer, this),
            (Expression value, Application application) => (
                outer,
                new Application(
                    new Abstraction(
                        application.Left.Symbol,
                        application.Left.Replacement.Evaluate(
                            Scope.inner(outer, Symbol, value)
                        ).expression),
                    application.Left.Symbol)),
            (Expression value, _) => (
                outer,
                Replacement
                    .Evaluate(Scope.inner(outer, Symbol, value))
                    .expression),
        };

    public static implicit operator Abstraction((Symbol left, Expression right) abstraction) =>
        new Abstraction(abstraction.left, abstraction.right);
    public override string ToString() => $"λ{Symbol}.{Replacement}";
}

class Application : Expression
{
    public Abstraction Left;
    public Expression Right;

    public Application(Abstraction left, Expression right)
    {
        Left = left;
        Right = right;
    }

    public override (Scope scope, Expression expression) Evaluate(Scope outer) =>
        (Left is Abstraction abstraction)
        // ? (scope, Left.Evaluate(Scope.inner(scope, abstraction.Symbol, Right)).expression)
        ? (outer, Left.Apply(outer, Right))
        : (outer, this);
    public override string ToString() => $"({Left} {Right})";
}

class Scope
{
    public Dictionary<Symbol, Expression> Values = new();

    public Scope(params KeyValuePair<Symbol, Expression>[] values)
    {
        foreach (var value in values) Values.Add(value.Key, value.Value);
    }
    public Scope(Dictionary<Symbol, Expression> values) : this(values.ToArray()) { }
    public Scope() { }

    public static Scope inner(Scope outer, Symbol s, Expression value) =>
        new Scope(outer.Values).Set(s, value);

    public Expression? Get(Symbol s) => Values.ContainsKey(s) ? Values[s] : null;

    public Scope Set(Symbol s, Expression? e)
    {
        // TODO: I think we need to check for use of symbol and rename incoming one on collision
        //       to avoid free/bound mixups, no?

        // (λx.(λy.xy) y) should be (λy.yz) (or sim. other name instead of z), not λy.yy
        // (y in function on left is different from y being sent into it)

        // e.g. [t/y] (λx.(λy.xy) y) -> (λx.(λt.xt) y) -> λt.yt
        // see p3 https://personal.utdallas.edu/~gupta/courses/apl/lambda.pdf

        if (e == null) return this;
        if (Values.ContainsKey(s)) Values[s] = e;
        else Values.Add(s, e);
        return this;
    }
}