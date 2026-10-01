using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Lab1;

class CFGBuilder
{
    private ControlFlowGraph _cfg = null!;
    private int _nextId;
    private BasicBlock _start = null!;
    private BasicBlock _end = null!;

    private class BreakableContext
    {
        public BasicBlock? Header; // куда идёт continue (только у циклов)
        public BasicBlock Exit = null!; // куда идёт break
        public bool IsLoop;
    }

    private void ConnectToEnd(BasicBlock current)
    {
        current = EnsureFreshBlock(current);
        current.Terminator = TerminatorKind.Return;
        _cfg.AddEdge(current, _end, EdgeKind.Return);
    }

    private void CollapseEmptyPassThroughBlocks()
    {
        bool changed = true;
        while (changed)
        {
            changed = false;

            foreach (var b in _cfg.Blocks.ToList())
            {
                if (b.Kind != BlockKind.Normal) continue;
                if (b.Instructions.Count > 0) continue;
                if (b.Terminator != TerminatorKind.Fallthrough) continue;
                if (b.Successors.Count != 1) continue;

                var succ = b.Successors[0];
                if (succ.Kind != EdgeKind.Fallthrough) continue;

                var target = succ.To;

                // убираем ребро b -> target
                target.Predecessors.Remove(succ);

                // перенаправляем всех предшественников b прямо в target
                foreach (var pred in b.Predecessors.ToList())
                {
                    pred.From.Successors.Remove(pred);

                    // не создаём дубликат, если такое ребро уже есть
                    bool exists = target.Predecessors.Any(e => e.From == pred.From && e.Kind == pred.Kind);

                    if (!exists)
                    {
                        var newEdge = new Edge(pred.From, target, pred.Kind, pred.Label);
                        pred.From.Successors.Add(newEdge);
                        target.Predecessors.Add(newEdge);
                    }
                }

                b.Predecessors.Clear();
                b.Successors.Clear();
                _cfg.Blocks.Remove(b);

                // если схлопнули Entry — обновим ссылку
                if (_cfg.Entry == b) _cfg.Entry = target;

                changed = true;
                break;
            }
        }
    }

    private readonly Stack<BreakableContext> _contexts = new();

    private BasicBlock NewBlock()
    {
        var b = new BasicBlock(_nextId++);
        _cfg.Blocks.Add(b);
        return b;
    }

    // === Точки входа ===

    public ControlFlowGraph Build(MethodDeclarationSyntax method)
    {
        _cfg = new ControlFlowGraph();
        _nextId = 0;
        _contexts.Clear();

        _start = NewBlock();
        _start.Kind = BlockKind.Start;
        _cfg.Start = _start;
        _end = NewBlock();
        _end.Kind = BlockKind.End;
        _cfg.End = _end;

        var entry = NewBlock();
        _cfg.Entry = entry;
        _cfg.AddEdge(_start, entry, EdgeKind.Fallthrough);

        if (method.Body != null)
        {
            var after = VisitStatement(method.Body, entry);
            if (after != null)
                ConnectToEnd(after);
        }
        else if (method.ExpressionBody != null)
        {
            entry = EnsureFreshBlock(entry);
            entry.Instructions.Add(new Instruction(
                $"return {method.ExpressionBody.Expression}", method.ExpressionBody.Expression));
            entry.Terminator = TerminatorKind.Return;
            _cfg.AddEdge(entry, _end, EdgeKind.Return);
        }
        else
        {
            ConnectToEnd(entry);
        }
        
        CollapseEmptyPassThroughBlocks();
        return _cfg;
    }

    public ControlFlowGraph Build(BlockSyntax body)
    {
        _cfg = new ControlFlowGraph();
        _nextId = 0;
        _contexts.Clear();

        _start = NewBlock();
        _start.Kind = BlockKind.Start;
        _cfg.Start = _start;
        _end = NewBlock();
        _end.Kind = BlockKind.End;
        _cfg.End = _end;

        var entry = NewBlock();
        _cfg.Entry = entry;
        _cfg.AddEdge(_start, entry, EdgeKind.Fallthrough);

        var after = VisitStatement(body, entry);
        if (after != null)
            ConnectToEnd(after);
        
        CollapseEmptyPassThroughBlocks();
        
        return _cfg;
    }

    // === Главный обход ===

    private BasicBlock? VisitStatement(StatementSyntax stmt, BasicBlock? current)
    {
        if (current == null) return null;

        switch (stmt)
        {
            case BlockSyntax block:
                return VisitList(block.Statements, current);

            case ExpressionStatementSyntax expr:
                current.Instructions.Add(new Instruction(expr.Expression.ToString(), expr.Expression));
                return current;

            case LocalDeclarationStatementSyntax decl:
                current.Instructions.Add(new Instruction(decl.ToString().Trim().TrimEnd(';'), decl));
                return current;

            case ReturnStatementSyntax ret:
                current = EnsureFreshBlock(current);
                current.Instructions.Add(new Instruction(
                    ret.Expression != null ? $"return {ret.Expression}" : "return", ret));
                current.Terminator = TerminatorKind.Return;
                _cfg.AddEdge(current, _end, EdgeKind.Return);
                return null;

            case IfStatementSyntax ifS:
                return VisitIf(ifS, current);

            case WhileStatementSyntax w:
                return VisitWhile(w, current);

            case DoStatementSyntax d:
                return VisitDo(d, current);

            case ForStatementSyntax f:
                return VisitFor(f, current);

            case ForEachStatementSyntax fe:
                return VisitForEach(fe, current);

            case BreakStatementSyntax:
                return VisitBreak(current);

            case ContinueStatementSyntax:
                return VisitContinue(current);

            case SwitchStatementSyntax sw:
                return VisitSwitch(sw, current);

            default:
                // fallback: неизвестный оператор — как одна инструкция
                current.Instructions.Add(new Instruction(stmt.ToString().Trim(), stmt));
                return current;
        }
    }

    private BasicBlock? VisitList(SyntaxList<StatementSyntax> statements, BasicBlock? current)
    {
        foreach (var s in statements)
        {
            if (current == null) break;
            current = VisitStatement(s, current);
        }

        return current;
    }

    // === if ===

    private BasicBlock? VisitIf(IfStatementSyntax ifS, BasicBlock current)
    {
        current = EnsureFreshBlock(current);
        current.Instructions.Add(new Instruction($"if ({ifS.Condition})", ifS.Condition));
        current.Terminator = TerminatorKind.Branch;

        var thenBlock = NewBlock();
        var mergeBlock = NewBlock();
        BasicBlock? elseBlock = ifS.Else != null ? NewBlock() : null;

        _cfg.AddEdge(current, thenBlock, EdgeKind.True, "true");
        _cfg.AddEdge(current, elseBlock ?? mergeBlock, EdgeKind.False, "false");

        var afterThen = VisitStatement(ifS.Statement, thenBlock);
        if (afterThen != null)
            _cfg.AddEdge(afterThen, mergeBlock, EdgeKind.Fallthrough);

        if (ifS.Else != null)
        {
            var afterElse = VisitStatement(ifS.Else.Statement, elseBlock!);
            if (afterElse != null)
                _cfg.AddEdge(afterElse, mergeBlock, EdgeKind.Fallthrough);
        }

        return mergeBlock;
    }

    // === while ===

    private BasicBlock? VisitWhile(WhileStatementSyntax w, BasicBlock current)
    {
        current = EnsureFreshBlock(current);
        var header = current;
        header.Instructions.Add(new Instruction($"while ({w.Condition})", w.Condition));
        header.Terminator = TerminatorKind.Branch;

        var body = NewBlock();
        var exit = NewBlock();

        _cfg.AddEdge(header, body, EdgeKind.True, "true");
        _cfg.AddEdge(header, exit, EdgeKind.False, "false");

        _contexts.Push(new BreakableContext { Header = header, Exit = exit, IsLoop = true });
        var afterBody = VisitStatement(w.Statement, body);
        _contexts.Pop();

        if (afterBody != null)
            _cfg.AddEdge(afterBody, header, EdgeKind.BackEdge);

        return exit;
    }

    // === do-while ===

    private BasicBlock? VisitDo(DoStatementSyntax d, BasicBlock current)
    {
        var body = NewBlock();
        var cond = NewBlock();
        var exit = NewBlock();

        _cfg.AddEdge(current, body, EdgeKind.Fallthrough);

        _contexts.Push(new BreakableContext { Header = cond, Exit = exit, IsLoop = true });
        var afterBody = VisitStatement(d.Statement, body);
        _contexts.Pop();

        if (afterBody != null)
            _cfg.AddEdge(afterBody, cond, EdgeKind.Fallthrough);

        cond.Instructions.Add(new Instruction($"while ({d.Condition})", d.Condition));
        cond.Terminator = TerminatorKind.Branch;

        _cfg.AddEdge(cond, body, EdgeKind.True, "true");
        _cfg.AddEdge(cond, exit, EdgeKind.False, "false");

        return exit;
    }

    // === for ===

    private BasicBlock? VisitFor(ForStatementSyntax f, BasicBlock current)
    {
        // init попадает в текущий блок
        if (f.Declaration != null)
            current.Instructions.Add(new Instruction(f.Declaration.ToString().TrimEnd(';'), f.Declaration));
        foreach (var init in f.Initializers)
            current.Instructions.Add(new Instruction(init.ToString(), init));

        var header = NewBlock();
        _cfg.AddEdge(current, header, EdgeKind.Fallthrough);

        var body = NewBlock();
        var incr = NewBlock();
        var exit = NewBlock();

        if (f.Condition != null)
        {
            header.Instructions.Add(new Instruction($"if ({f.Condition})", f.Condition));
            header.Terminator = TerminatorKind.Branch;
            _cfg.AddEdge(header, body, EdgeKind.True, "true");
            _cfg.AddEdge(header, exit, EdgeKind.False, "false");
        }
        else
        {
            // for (;;) — бесконечный
            header.Terminator = TerminatorKind.Goto;
            _cfg.AddEdge(header, body, EdgeKind.Fallthrough);
        }

        // ВАЖНО: continue в for идёт в incr, а не в header
        _contexts.Push(new BreakableContext { Header = incr, Exit = exit, IsLoop = true });
        var afterBody = VisitStatement(f.Statement, body);
        _contexts.Pop();

        if (afterBody != null)
            _cfg.AddEdge(afterBody, incr, EdgeKind.Fallthrough);

        foreach (var inc in f.Incrementors)
            incr.Instructions.Add(new Instruction(inc.ToString(), inc));
        _cfg.AddEdge(incr, header, EdgeKind.BackEdge);

        return exit;
    }

    // === foreach ===

    private BasicBlock? VisitForEach(ForEachStatementSyntax fe, BasicBlock current)
    {
        current = EnsureFreshBlock(current);
        var header = current;
        header.Instructions.Add(new Instruction(
            $"foreach ({fe.Type} {fe.Identifier} in {fe.Expression})", fe));
        header.Terminator = TerminatorKind.Branch;

        var body = NewBlock();
        var exit = NewBlock();

        _cfg.AddEdge(header, body, EdgeKind.True, "есть элемент");
        _cfg.AddEdge(header, exit, EdgeKind.False, "конец");

        _contexts.Push(new BreakableContext { Header = header, Exit = exit, IsLoop = true });
        var afterBody = VisitStatement(fe.Statement, body);
        _contexts.Pop();

        if (afterBody != null)
            _cfg.AddEdge(afterBody, header, EdgeKind.BackEdge);

        return exit;
    }

    // === break / continue ===

    private BasicBlock? VisitBreak(BasicBlock current)
    {
        if (_contexts.Count == 0)
            throw new InvalidOperationException("break вне цикла или switch");

        current = EnsureFreshBlock(current);
        var ctx = _contexts.Peek();
        current.Instructions.Add(new Instruction("break"));
        current.Terminator = TerminatorKind.Goto;
        _cfg.AddEdge(current, ctx.Exit, EdgeKind.Break);
        return null;
    }

    private BasicBlock? VisitContinue(BasicBlock current)
    {
        var loop = _contexts.FirstOrDefault(c => c.IsLoop);
        if (loop == null)
            throw new InvalidOperationException("continue вне цикла");

        current = EnsureFreshBlock(current);
        current.Instructions.Add(new Instruction("continue"));
        current.Terminator = TerminatorKind.Goto;
        _cfg.AddEdge(current, loop.Header!, EdgeKind.Continue);
        return null;
    }

    // === switch ===

    private BasicBlock? VisitSwitch(SwitchStatementSyntax sw, BasicBlock current)
    {
        current = EnsureFreshBlock(current);
        current.Instructions.Add(new Instruction($"switch ({sw.Expression})", sw.Expression));
        current.Terminator = TerminatorKind.Branch;

        var exit = NewBlock();
        _contexts.Push(new BreakableContext { Exit = exit, IsLoop = false });

        foreach (var section in sw.Sections)
        {
            var caseBlock = NewBlock();
            string label = string.Join(", ",
                section.Labels.Select(l => l.ToString().TrimEnd(':', ' ')));

            _cfg.AddEdge(current, caseBlock, EdgeKind.Fallthrough, label);

            var afterCase = VisitList(section.Statements, caseBlock);
            if (afterCase != null)
                _cfg.AddEdge(afterCase, exit, EdgeKind.Fallthrough);
        }

        _contexts.Pop();
        return exit;
    }

    private BasicBlock EnsureFreshBlock(BasicBlock current)
    {
        if (current.Instructions.Count == 0)
            return current;

        var fresh = NewBlock();
        _cfg.AddEdge(current, fresh, EdgeKind.Fallthrough);
        return fresh;
    }
}

class LoopContext
{
    public BasicBlock Header;
    public BasicBlock Exit;
}

class SwitchContext
{
    public BasicBlock Exit;
    public BasicBlock? Default;
}