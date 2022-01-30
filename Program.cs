var state = new State(
    ('x', (('y', 'x'), 'y')),
    ('x', 'x'),
    'z'
);

var next = state.Evaluate();
while (next != state)
{
    Console.WriteLine(state);
    state = next;
    next = state.Evaluate();
}
Console.WriteLine(state);

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

    public static implicit operator Expression(char identifier) => new Symbol(identifier);
    public static implicit operator Expression((Symbol left, Expression right) abstraction) =>
        new Abstraction(abstraction.left, abstraction.right);
    public static implicit operator Expression((Abstraction left, char right) application) =>
        new Application(application.left, application.right);
}

class Symbol : Expression
{
    public char Identifier;

    public Symbol(char identifier)
    {
        Identifier = identifier;
    }

    public override (Scope, Expression) Evaluate(Scope scope) =>
        scope.Get(this) switch
        {
            null => (scope, this),
            Expression e => (scope, e)
        };

    public static implicit operator Symbol(char identifier) => new Symbol(identifier);
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

    private Scope inner(Scope outer, Symbol s, Expression value) =>
        new Scope(outer.Values).Set(Symbol, value);

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
                            inner(outer, Symbol, value)
                        ).expression),
                    application.Left.Symbol)),
            (Expression value, _) => (
                outer,
                Replacement
                    .Evaluate(inner(outer, Symbol, value))
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

    public override (Scope scope, Expression expression) Evaluate(Scope scope) =>
        (scope, Left.Evaluate(new Scope(scope.Values).Set(Left.Symbol, Right)).expression);
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

    public Expression? Get(Symbol s) => Values.ContainsKey(s) ? Values[s] : null;

    public Scope Set(Symbol s, Expression? e)
    {
        if (e == null) return this;
        if (Values.ContainsKey(s)) Values[s] = e;
        else Values.Add(s, e);
        return this;
    }
}