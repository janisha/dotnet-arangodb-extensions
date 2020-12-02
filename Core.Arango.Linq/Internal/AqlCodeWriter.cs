﻿using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Core.Arango.Linq.Internal.Util.Extensions;
using static Core.Arango.Linq.Internal.Globals;
using static Core.Arango.Linq.Internal.Util.Functions;
using static Core.Arango.Linq.Internal.Util.Methods;
using static System.Linq.Enumerable;
using static System.Linq.Expressions.ExpressionType;
using static System.Linq.Expressions.GotoExpressionKind;
using static Core.Arango.Linq.Internal.CSharpMultilineBlockTypes;
using static Core.Arango.Linq.Internal.CSharpBlockMetadata;

#nullable enable

namespace Core.Arango.Linq.Internal
{
    internal class AqlCodeWriter : WriterBase
    {
        // Dictionary mit binären Operatoren
        private static readonly Dictionary<ExpressionType, string> simpleBinaryOperators =
            new Dictionary<ExpressionType, string>
            {
                [Add] = "+",
                [AddChecked] = "+",
                [Divide] = "/",
                [Modulo] = "%",
                [Multiply] = "*",
                [MultiplyChecked] = "*",
                [Subtract] = "-",
                [SubtractChecked] = "-",
                [And] = "&",
                [Or] = "|",
                [ExclusiveOr] = "^",
                [AndAlso] = "&&",
                [OrElse] = "||",
                [Equal] = "==",
                [NotEqual] = "!=",
                [GreaterThanOrEqual] = ">=",
                [GreaterThan] = ">",
                [LessThan] = "<",
                [LessThanOrEqual] = "<=",
                [Coalesce] = "??",
                [LeftShift] = "<<",
                [RightShift] = ">>",
                [Assign] = "=",
                [AddAssign] = "+=",
                [AddAssignChecked] = "+=",
                [AndAssign] = "&=",
                [DivideAssign] = "/=",
                [ExclusiveOrAssign] = "^=",
                [LeftShiftAssign] = "<<=",
                [ModuloAssign] = "%=",
                [MultiplyAssign] = "*=",
                [MultiplyAssignChecked] = "*=",
                [OrAssign] = "|=",
                [RightShiftAssign] = ">>=",
                [SubtractAssign] = "-=",
                [SubtractAssignChecked] = "-="
            };

        public Dictionary<string, object> BindVars = new Dictionary<string, object>();

        public AqlCodeWriter(object o) : base(o, FormatterNames.CSharp)
        {
        }

        public AqlCodeWriter(object o, out Dictionary<string, (int start, int length)> pathSpans) : base(o,
            FormatterNames.CSharp, out pathSpans)
        {
        }

        /// <summary>
        /// Schreibt einen Index-Zugriff
        /// </summary>
        /// <param name="instancePath"></param>
        /// <param name="instance">die Exoression</param>
        /// <param name="argBasePath"></param>
        /// <param name="keys"></param>
        private void WriteIndexerAccess(string instancePath, Expression instance, string argBasePath,
            params Expression[] keys)
        {
            WriteNode(instancePath, instance);
            Write("[");
            WriteNodes(argBasePath, keys);
            Write("]");
        }

        /// <summary>
        /// Stößt das Schreiben eines Index-Zugriffs an.
        /// </summary>
        /// <param name="instancePath"></param>
        /// <param name="instance">die Expression</param>
        /// <param name="argBasePath"></param>
        /// <param name="keys"></param>
        private void WriteIndexerAccess(string instancePath, Expression instance, string argBasePath,
            IEnumerable<Expression> keys)
        {
            WriteIndexerAccess(instancePath, instance, argBasePath, keys.ToArray());
        }

        /// <summary>
        /// Überprüft den Typ eines binären Operators und stößt die entsprechenden Schreib-Operationen an
        /// </summary>
        /// <param name="nodeType">Nodetyp der Expression</param>
        /// <param name="leftPath"></param>
        /// <param name="left">linker Teil des binären Ausdrucks</param>
        /// <param name="rightPath"></param>
        /// <param name="right">rechter Teil des binären Ausdrucks</param>
        private void WriteBinary(ExpressionType nodeType, string leftPath, Expression left, string rightPath,
            Expression right)
        {
            if (simpleBinaryOperators.TryGetValue(nodeType, out var @operator))
            {
                WriteNode(leftPath, left);
                Write($" {@operator} ");

                // falls es sich um einen string als rechte Seite des binären Ausdrucks handelt, wird er in eine BindVar umgewandelt, um SQL-Injection vorzubeugen. todo: muss auch für die linke Seite gelten
                if (right.Type == typeof(string))
                {
                    var value = right.ExtractValue();
                    AddAndWriteBindVar(value);
                }
                else
                {
                    WriteNode(rightPath, right);
                }


                return;
            }

            switch (nodeType)
            {
                case ArrayIndex:
                    WriteNode(leftPath, left);
                    Write("[");
                    WriteNode(rightPath, right);
                    Write("]");
                    return;
                case Power:
                    Write("Math.Pow(");
                    WriteNode(leftPath, left);
                    Write(", ");
                    WriteNode(rightPath, right);
                    Write(")");
                    return;
                case PowerAssign:
                    WriteNode($"{leftPath}_0", left);
                    Write(" = ");
                    Write("Math.Pow(");
                    WriteNode(leftPath, left);
                    Write(", ");
                    WriteNode(rightPath, right);
                    Write(")");
                    return;
            }

            throw new NotImplementedException();
        }

        /// <summary>
        /// Stoößt das Schreiben eines binären Ausdrucks an
        /// </summary>
        /// <param name="expr">die Expression</param>
        protected override void WriteBinary(BinaryExpression expr)
        {
            WriteBinary(expr.NodeType, "Left", expr.Left, "Right", expr.Right);
        }

        /// <summary>
        /// Überprüft den Typ eines unären Operators und stößt die entsprechenden Schreib-Operationen an
        /// </summary>
        /// <param name="nodeType">Typ des Ausdrucks</param>
        /// <param name="operandPath"></param>
        /// <param name="operand">Operand</param>
        /// <param name="type"></param>
        /// <param name="expressionTypename">Name des Ausdrucks</param>
        private void WriteUnary(ExpressionType nodeType, string operandPath, Expression operand, Type type,
            string expressionTypename)
        {
            switch (nodeType)
            {
                case ArrayLength:
                    WriteNode(operandPath, operand);
                    Write(".Length");
                    break;
                case ExpressionType.Convert:
                case ConvertChecked:
                case Unbox:
                    if (!type.IsAssignableFrom(operand.Type)) Write($"({type.FriendlyName(language)})");
                    WriteNode(operandPath, operand);
                    break;
                case Negate:
                case NegateChecked:
                    Write("-");
                    WriteNode(operandPath, operand);
                    break;
                case Not:
                    if (type == typeof(bool))
                        Write("!");
                    else
                        Write("~");
                    WriteNode(operandPath, operand);
                    break;
                case OnesComplement:
                    Write("~");
                    WriteNode(operandPath, operand);
                    break;
                case TypeAs:
                    WriteNode(operandPath, operand);
                    Write($" as {type.FriendlyName(language)}");
                    break;
                case PreIncrementAssign:
                    Write("++");
                    WriteNode(operandPath, operand);
                    break;
                case PostIncrementAssign:
                    WriteNode(operandPath, operand);
                    Write("++");
                    break;
                case PreDecrementAssign:
                    Write("--");
                    WriteNode(operandPath, operand);
                    break;
                case PostDecrementAssign:
                    WriteNode(operandPath, operand);
                    Write("--");
                    break;
                case IsTrue:
                    WriteNode(operandPath, operand);
                    break;
                case IsFalse:
                    Write("!");
                    WriteNode(operandPath, operand);
                    break;
                case Increment:
                    WriteNode(operandPath, operand);
                    Write(" += 1");
                    break;
                case Decrement:
                    WriteNode(operandPath, operand);
                    Write(" -= 1");
                    break;
                case Throw:
                    Write("throw");
                    if (operand != null)
                    {
                        Write(" ");
                        WriteNode(operandPath, operand);
                    }

                    break;
                case Quote:
                    TrimEnd(true);
                    WriteEOL();
                    //Write("// --- Quoted - begin");
                    Indent();
                    WriteEOL();
                    WriteNode(operandPath, operand);
                    WriteEOL(true);
                    //Write("// --- Quoted - end");
                    break;
                case UnaryPlus:
                    Write("+");
                    WriteNode(operandPath, operand);
                    break;
                default:
                    throw new NotImplementedException(
                        $"NodeType: {nodeType}, Expression object type: {expressionTypename}");
            }
        }

        /// <summary>
        /// Stößt das Schreiben eines unären Ausdrucks an
        /// </summary>
        /// <param name="expr"></param>
        protected override void WriteUnary(UnaryExpression expr)
        {
            WriteUnary(expr.NodeType, "Operand", expr.Operand, expr.Type, expr.GetType().Name);
        }

        /// <summary>
        /// Schreibt einen Lambda-Ausdruck
        /// </summary>
        /// <param name="expr">die Expression</param>
        protected override void WriteLambda(LambdaExpression expr)
        {
            //Write("(");
            WriteNodes("Parameters", expr.Parameters, false, ", ", true);
            //Write(") => ");

            if (CanInline(expr.Body))
            {
                WriteNode("Body", expr.Body);
                return;
            }

            Write("{");
            Indent();
            WriteEOL();

            var blockType = CSharpMultilineBlockTypes.Block;
            if (expr.Body.Type != typeof(void))
            {
                if (expr.Body is BlockExpression bexpr && bexpr.HasMultipleLines())
                    blockType = CSharpMultilineBlockTypes.Return;
                else
                    Write("return ");
            }

            WriteNode("Body", expr.Body, CreateMetadata(blockType));
            WriteStatementEnd(expr.Body);
            WriteEOL(true);
            Write("}");
        }


        /// <summary>
        /// Schreibt eine Parameter-Deklaration
        /// </summary>
        /// <param name="prm">der Parameter-Ausdruck</param>
        protected override void WriteParameterDeclarationImpl(ParameterExpression prm)
        {
            if(string.IsNullOrEmpty(Collection)) Collection = prm.Type.Name;
            Iterator = prm.Name;

            // TODO: Removed
            //if (prm.IsByRef) Write("ref ");
            //Write($"{prm.Type.FriendlyName(language)} {prm.Name}");
        }

        /// <summary>
        /// Schreibt einen Parameter
        /// </summary>
        /// <param name="expr">der Parameter-Ausdruck</param>
        protected override void WriteParameter(ParameterExpression expr)
        {
            Write(expr.Name);
        }

        /// <summary>
        /// Schreibt eine Konstante
        /// </summary>
        /// <param name="expr">der Konstanten-Ausdruck</param>
        protected override void WriteConstant(ConstantExpression expr)
        {
            var literal = RenderLiteral(expr.Value, language);
            if (literal.Length > 0)
            {
                Write(literal);
            }
            // Write(RenderLiteral(expr.Value, language));
        }

        /// <summary>
        /// Schreibt einen Member-Zugriff und fügt die Variable den BindVars hinzu
        /// </summary>
        /// <param name="expr">die Member-Expression</param>
        protected override void WriteMemberAccess(MemberExpression expr)
        {
            switch (expr.Expression)
            {
                case ConstantExpression cexpr when cexpr.Type.IsClosureClass():
                case MemberExpression mexpr when mexpr.Type.IsClosureClass():

                    var paramter = expr.Member.Name.Replace("$VB$Local_", "");
                    var value = expr.ExtractValue();

                    if (!BindVars.ContainsKey(paramter))
                        BindVars.Add(paramter, value);

                    Write("@" + paramter);
                    return;
                case null:
                    var className = expr.Member.DeclaringType;

                    var param = expr.Member.Name;
                    var val = expr.ExtractValue();
                    if (!BindVars.ContainsKey(param))
                        BindVars.Add(param, val);

                    Write("@" + param);
                    return;

                    // static member
                    //Write($"{expr.Member.DeclaringType.FriendlyName(language)}.{expr.Member.Name}");
                    //return;
                default:
                    WriteNode("Expression", expr.Expression);
                    var memberName = expr.Member.Name;
                    // todo: ersetzen mit caseignore (memberName.equals.....)
                    if (memberName == "Key" || memberName == "key")
                    {
                        Write("._key");
                        return;
                    }
                    Write($".{expr.Member.Name}");
                    return;
            }
        }

        /// <summary>
        /// Schreibt ein neues Objekt
        /// </summary>
        /// <param name="type"></param>
        /// <param name="argsPath"></param>
        /// <param name="args"></param>
        private void WriteNew(Type type, string argsPath, IList<Expression> args)
        {
            //Write("new ");
            //Write(type.FriendlyName(language));
            Write(" {");
            WriteNodes(argsPath, args);
            Write("}");
        }

        /// <summary>
        /// Schreibt ein neues Objekt inklusive Initialisierung von Werten
        /// </summary>
        /// <param name="expr">die Expression</param>
        protected override void WriteNew(NewExpression expr)
        {
            SelectType = expr.Type;

            if (expr.Type.IsAnonymous())
            {
                Write(" {");
                Indent();
                WriteEOL();
                expr.Constructor.GetParameters().Select(x => x.Name).Zip(expr.Arguments).ForEachT((name, arg, index) =>
                {
                    if (index > 0)
                    {
                        Write(",");
                        WriteEOL();
                    }

                    // write as `property = member` only if the source name is different from the target name
                    // otheriwse just write `member`
                    if (!(arg is MemberExpression mexpr && mexpr.Member.Name.Replace("$VB$Local_", "") == name))
                        Write($"{name} = ");
                    WriteNode($"Arguments[{index}]", arg);
                });
                WriteEOL(true);
                Write("}");
                return;
            }

            WriteNew(expr.Type, "Arguments", expr.Arguments);
        }

        /// <summary>
        /// Schreibt einen Aufruf
        /// </summary>
        /// <param name="expr">die Expression des Methoden-Aufrufs</param>
        protected override void WriteCall(MethodCallExpression expr)
        {
            if (expr.Method.In(stringConcats))
            {
                var firstArg = expr.Arguments[0];
                IEnumerable<Expression>? argsToWrite = null;
                var argsPath = "";
                if (firstArg is NewArrayExpression newArray && firstArg.NodeType == NewArrayInit)
                {
                    argsToWrite = newArray.Expressions;
                    argsPath = "Arguments[0].Expressions";
                }
                else if (expr.Arguments.All(x => x.Type == typeof(string)))
                {
                    argsToWrite = expr.Arguments;
                    argsPath = "Arguments";
                }

                if (argsToWrite != null)
                {
                    WriteNodes(argsPath, argsToWrite, " + ");
                    return;
                }
            }

            var isIndexer = false;
            if ((expr.Object?.Type.IsArray ?? false) && expr.Method.Name == "Get")
                isIndexer = true;
            else
                isIndexer = expr.Method.IsIndexerMethod();
            if (isIndexer)
            {
                // if the instance is null; it usually means it's a static member access
                // but there is no such thing as a static indexer
                WriteIndexerAccess("Object", expr.Object!, "Arguments", expr.Arguments);
                return;
            }

            if (expr.Method.In(stringFormats) && expr.Arguments[0] is ConstantExpression cexpr &&
                cexpr.Value is string format)
            {
                var parts = ParseFormatString(format);
                Write("$\"");
                foreach (var (literal, index, alignment, itemFormat) in parts)
                {
                    Write(literal.Replace("{", "{{").Replace("}", "}}"));
                    if (index == null) break;
                    Write("{");
                    WriteNode($"Arguments[{index.Value + 1}]", expr.Arguments[index.Value + 1]);
                    if (alignment != null) Write($", {alignment}");
                    if (itemFormat != null) Write($":{itemFormat}");
                    Write("}");
                }

                Write("\"");
                return;
            }

            var (path, o) = ("Object", expr.Object);
            var arguments = expr.Arguments.Select((x, index) => ($"Arguments[{index}]", x));

            if (expr.Object is null && expr.Method.HasAttribute<ExtensionAttribute>())
            {
                (path, o) = ("Arguments[0]", expr.Arguments[0]);
                arguments = expr.Arguments.Skip(1).Select((x, index) => ($"Arguments[{index + 1}]", x));
            }

            if (o is null) // static non-extension method -- write the type name
                Write(expr.Method.ReflectedType.FriendlyName(language));
            // im Falle eines Contains muss die Reihenfolge von LINQ und AQL getauscht werden
            else if (expr.Method.Name.Equals("contains", StringComparison.InvariantCultureIgnoreCase))
            {
                WriteNodes(arguments);
                Write("\nIN ");
                WriteNode(path, o);
            }
            else // instance method, or extension method
                WriteNode(path, o);

            var name = expr.Method.Name;

            if (name.Equals("where", StringComparison.InvariantCultureIgnoreCase))
            {
                Write("\nFILTER ");
                WriteNodes(arguments);
            }

            else if (name.Equals("orderby", StringComparison.InvariantCultureIgnoreCase))
            {
                Write("\nSORT ");
                WriteNodes(arguments);
                Write(" ASC");
            }

            else if (name.Equals("thenby", StringComparison.InvariantCultureIgnoreCase))
            {
                Write(", ");
                WriteNodes(arguments);
                Write(" ASC");
            }


            else if (name.Equals("orderbydescending", StringComparison.InvariantCultureIgnoreCase))
            {
                Write("\nSORT ");
                WriteNodes(arguments);
                Write(" DESC");
            }

            else if (name.Equals("thenbydescending", StringComparison.InvariantCultureIgnoreCase))
            {
                Write(", ");
                WriteNodes(arguments);
                Write(" DESC");
            }

            else if (name.Equals("select", StringComparison.InvariantCultureIgnoreCase))
            {
                Write("\nRETURN ");
                WriteNodes(arguments);
            }

            else if (name.Equals("groupby", StringComparison.InvariantCultureIgnoreCase))
            {
                Write("\nCOLLECT ");
                WriteNodes(arguments);
            }

            else if (name.Equals("singleordefault", StringComparison.InvariantCultureIgnoreCase))
            {
                Write("\nFILTER ");
                WriteNodes(arguments);
                Write("\nRETURN " + Iterator);
            }

            else if (name.Equals("contains", StringComparison.InvariantCultureIgnoreCase))
            {}

            else if (name.Equals("startswith", StringComparison.InvariantCultureIgnoreCase))
            {
                Write("\nLIKE ");
                
                // parse the enumerable to a easy-to-access list
                var argument = arguments.ToList();

                // Since argument here can only contain one entry, [0] can be used and stringified. The " then needs to be replaced as this will be done via the bind parameters.
                var literal = argument[0].x.ToString().Replace("\"", "");

                // Since we use "startswith", a "%" needs to be added to the end of the string.
                AddAndWriteBindVar(string.Concat(literal, "%"));
            }

            else if (name.Equals("take", StringComparison.InvariantCultureIgnoreCase))
            {
                var argument = arguments.First();

                var value = int.Parse(argument.x.ToString());
                Write($"\nLIMIT {value}");
            }

            else if (name.Equals("skip", StringComparison.InvariantCultureIgnoreCase))
            {
                var argument = arguments.First();

                var value = int.Parse(argument.x.ToString());               

                InsetSkipForLimit($" {value}, ");
            }


            else
            {
                Write($".{name}(");
                WriteNodes(arguments);
                Write(")");
            }
        }

        /// <summary>
        /// Schreibt eine Zuweisung
        /// </summary>
        /// <param name="binding">der Zuweisungs-Ausdruck</param>
        protected override void WriteBinding(MemberBinding binding)
        {
            Write(binding.Member.Name);
            Write(" = ");
            if (binding is MemberAssignment assignmentBinding)
            {
                WriteNode("Expression", assignmentBinding.Expression);
                return;
            }

            Write("{");

            IEnumerable<object>? items = null;
            var itemsPath = "";
            switch (binding)
            {
                case MemberListBinding listBinding when listBinding.Initializers.Count > 0:
                    items = listBinding.Initializers;
                    itemsPath = "Initializers";
                    break;
                case MemberMemberBinding memberBinding when memberBinding.Bindings.Count > 0:
                    items = memberBinding.Bindings;
                    itemsPath = "Bindings";
                    break;
            }

            if (items != null)
            {
                Indent();
                WriteEOL();
                WriteNodes(itemsPath, items, true);
                WriteEOL(true);
            }

            Write("}");
        }

        /// <summary>
        /// Schreibt eine Member-Initialisierung
        /// </summary>
        /// <param name="expr">die expression</param>
        protected override void WriteMemberInit(MemberInitExpression expr)
        {
            WriteNode("NewExpression", expr.NewExpression);
            if (expr.Bindings.Count > 0)
            {
                Write(" {");
                Indent();
                WriteEOL();
                WriteNodes("Bindings", expr.Bindings, true);
                WriteEOL(true);
                Write("}");
            }
        }

        /// <summary>
        /// Schreibt eine Listen-Initialisierung
        /// </summary>
        /// <param name="expr">der Ausdruck</param>
        protected override void WriteListInit(ListInitExpression expr)
        {
            WriteNode("NewExpression", expr.NewExpression);
            Write(" {");
            Indent();
            WriteEOL();
            WriteNodes("Initializers", expr.Initializers, true);
            WriteEOL(true);
            Write("}");
        }

        /// <summary>
        /// Schreibt die Initialisierung eines Elements
        /// </summary>
        /// <param name="elementInit"></param>
        protected override void WriteElementInit(ElementInit elementInit)
        {
            var args = elementInit.Arguments;
            switch (args.Count)
            {
                case 0:
                    throw new NotImplementedException();
                case 1:
                    WriteNode("Arguments[0]", args[0]);
                    break;
                default:
                    Write("{");
                    Indent();
                    WriteEOL();
                    WriteNodes("Arguments", args, true);
                    WriteEOL(true);
                    Write("}");
                    break;
            }
        }

        /// <summary>
        /// Schreibt die Erstellung eines neuen Arrays
        /// </summary>
        /// <param name="expr"></param>
        protected override void WriteNewArray(NewArrayExpression expr)
        {
            switch (expr.NodeType)
            {
                case NewArrayInit:
                    var elementType = expr.Type.GetElementType();
                    Write("new");
                    if (elementType.IsArray || expr.Expressions.None() ||
                        expr.Expressions.Any(x => x.Type != elementType))
                    {
                        Write(" ");
                        Write(expr.Type.FriendlyName(language));
                    }
                    else
                    {
                        Write("[]");
                    }

                    Write(" { ");
                    WriteNodes("Expressions", expr.Expressions);
                    Write(" }");
                    break;
                case NewArrayBounds:
                    var (left, right) = ("[", "]");
                    var nestedArrayTypes = expr.Type.NestedArrayTypes().ToList();
                    Write($"new {nestedArrayTypes.Last().root!.FriendlyName(language)}");
                    nestedArrayTypes.ForEachT((current, _, index) =>
                    {
                        Write(left);
                        if (index == 0)
                            WriteNodes("Expressions", expr.Expressions);
                        else
                            Write(Repeat("", current.GetArrayRank()).Joined());
                        Write(right);
                    });
                    break;
                default:
                    throw new NotImplementedException();
            }
        }

        private bool CanInline(Expression expr)
        {
            switch (expr)
            {
                case ConditionalExpression cexpr when cexpr.Type == typeof(void):
                case BlockExpression bexpr when
                    bexpr.Expressions.Count > 1 ||
                    bexpr.Variables.Any() ||
                    bexpr.Expressions.Count == 1 && CanInline(bexpr.Expressions.First()):
                case SwitchExpression _:
                case LambdaExpression _:
                case TryExpression _:
                case Expression _ when expr.NodeType == Quote:
                    return false;
                case RuntimeVariablesExpression _:
                    throw new NotImplementedException();
            }

            return true;
        }

        /// <summary>
        /// Schreibt einen konditionalen Ausdruck
        /// </summary>
        /// <param name="expr">die Expression</param>
        /// <param name="metadata"></param>
        protected override void WriteConditional(ConditionalExpression expr, object? metadata)
        {
            if (expr.Type != typeof(void))
            {
                WriteNode("Test", expr.Test);
                Write(" ? ");
                WriteNode("IfTrue", expr.IfTrue);
                Write(" : ");
                WriteNode("IfFalse", expr.IfFalse);
                return;
            }

            Write("if (");
            WriteNode("Test", expr.Test, CreateMetadata(Test));
            Write(") {");
            Indent();
            WriteEOL();
            WriteNode("IfTrue", expr.IfTrue, CreateMetadata(CSharpMultilineBlockTypes.Block));
            WriteStatementEnd(expr.IfTrue);
            WriteEOL(true);
            Write("}");
            if (!expr.IfFalse.IsEmpty())
            {
                Write(" else ");
                if (!(expr.IfFalse is ConditionalExpression))
                {
                    Write("{");
                    Indent();
                    WriteEOL();
                }

                WriteNode("IfFalse", expr.IfFalse, CreateMetadata(CSharpMultilineBlockTypes.Block));
                WriteStatementEnd(expr.IfFalse);
                if (!(expr.IfFalse is ConditionalExpression))
                {
                    WriteEOL(true);
                    Write("}");
                }
            }
        }

        /// <summary>
        /// Schreibt ein default
        /// </summary>
        /// <param name="expr">die Expression</param>
        protected override void WriteDefault(DefaultExpression expr)
        {
            Write($"default({expr.Type.FriendlyName(language)})");
        }

        protected override void WriteTypeBinary(TypeBinaryExpression expr)
        {
            WriteNode("Expression", expr.Expression);
            var typeName = expr.TypeOperand.FriendlyName(language);
            switch (expr.NodeType)
            {
                case TypeIs:
                    Write($" is {typeName}");
                    break;
                case TypeEqual:
                    Write($".GetType() == typeof({typeName})");
                    break;
            }
        }

        /// <summary>
        /// Schreibt einen Aufruf
        /// </summary>
        /// <param name="expr">die Expression</param>
        protected override void WriteInvocation(InvocationExpression expr)
        {
            if (expr.Expression is LambdaExpression) Write("(");
            WriteNode("Expression", expr.Expression);
            if (expr.Expression is LambdaExpression) Write(")");
            Write("(");
            WriteNodes("Arguments", expr.Arguments);
            Write(")");
        }

        /// <summary>
        /// Stößt das Schreiben eines Index-Zugriffs auf
        /// </summary>
        /// <param name="expr"></param>
        protected override void WriteIndex(IndexExpression expr)
        {
            WriteIndexerAccess("Object", expr.Object, "Arguments", expr.Arguments);
        }

        protected override void WriteBlock(BlockExpression expr, object? metadata)
        {
            var (blockType, parentIsBlock) = metadata as CSharpBlockMetadata ?? CreateMetadata();
            bool introduceNewBlock;
            if (blockType.In(CSharpMultilineBlockTypes.Block, CSharpMultilineBlockTypes.Return))
            {
                introduceNewBlock = expr.Variables.Any() && parentIsBlock;
                if (introduceNewBlock)
                {
                    Write("{");
                    Indent();
                    WriteEOL();
                }

                expr.Variables.ForEach((subexpr, index) =>
                {
                    WriteNode($"Variables[{index}]", subexpr, true);
                    Write(";");
                    WriteEOL();
                });
                expr.Expressions.ForEach((subexpr, index) =>
                {
                    if (index > 0) WriteEOL();
                    if (subexpr is LabelExpression) TrimEnd();
                    if (blockType == CSharpMultilineBlockTypes.Return && index == expr.Expressions.Count - 1)
                    {
                        if (subexpr is BlockExpression bexpr && bexpr.HasMultipleLines())
                        {
                            WriteNode($"Expressions[{index}]", subexpr,
                                CreateMetadata(CSharpMultilineBlockTypes.Return, true));
                        }
                        else
                        {
                            Write("return ");
                            WriteNode($"Expressions[{index}]", subexpr,
                                CreateMetadata(CSharpMultilineBlockTypes.Block, true));
                        }
                    }
                    else
                    {
                        WriteNode($"Expressions[{index}]", subexpr,
                            CreateMetadata(CSharpMultilineBlockTypes.Block, true));
                    }

                    WriteStatementEnd(subexpr);
                });
                if (introduceNewBlock)
                {
                    WriteEOL(true);
                    Write("}");
                }

                return;
            }

            introduceNewBlock =
                expr.Expressions.Count > 1 && !parentIsBlock ||
                expr.Variables.Any();
            if (introduceNewBlock)
            {
                if (blockType == Inline || parentIsBlock) Write("(");
                Indent();
                WriteEOL();
            }

            WriteNodes("Variables", expr.Variables, true, ",", true);
            expr.Expressions.ForEach((subexpr, index) =>
            {
                if (index > 0 || expr.Variables.Count > 0)
                {
                    var previousExpr = index > 0 ? expr.Expressions[index - 1] : null;
                    if (previousExpr is null ||
                        !(previousExpr is LabelExpression || subexpr is RuntimeVariablesExpression)) Write(",");
                    WriteEOL();
                }

                if (subexpr is LabelExpression) TrimEnd();
                WriteNode($"Expressions[{index}]", subexpr, CreateMetadata(Test, true));
            });
            if (introduceNewBlock)
            {
                WriteEOL(true);
                if (blockType == Inline || parentIsBlock) Write(")");
            }
        }

        /// <summary>
        /// Schreibt das Ende eines Statements
        /// </summary>
        /// <param name="expr">die Expression</param>
        private void WriteStatementEnd(Expression expr)
        {
            switch (expr)
            {
                case ConditionalExpression cexpr when cexpr.Type == typeof(void):
                case BlockExpression _:
                case SwitchExpression _:
                case LabelExpression _:
                case TryExpression _:
                case RuntimeVariablesExpression _:
                case UnaryExpression bexpr when bexpr.NodeType == Quote:
                    return;
            }

            Write(";");
        }

        /// <summary>
        /// Schreibt einen Switch-Case
        /// </summary>
        /// <param name="switchCase"></param>
        protected override void WriteSwitchCase(SwitchCase switchCase)
        {
            switchCase.TestValues.ForEach((testValue, index) =>
            {
                if (index > 0) WriteEOL();
                Write("case ");
                WriteNode($"TestValues[{index}]", testValue);
                Write(":");
            });
            Indent();
            WriteEOL();
            WriteNode("Body", switchCase.Body, CreateMetadata(CSharpMultilineBlockTypes.Block));
            WriteStatementEnd(switchCase.Body);
            WriteEOL();
            Write("break;");
        }

        protected override void WriteSwitch(SwitchExpression expr)
        {
            Write("switch (");
            WriteNode("SwitchValue", expr.SwitchValue, CreateMetadata(Test));
            Write(") {");
            Indent();
            WriteEOL();
            expr.Cases.ForEach((switchCase, index) =>
            {
                if (index > 0) WriteEOL();
                WriteNode($"Cases[{index}]", switchCase);
                Dedent();
            });
            if (expr.DefaultBody != null)
            {
                WriteEOL();
                Write("default:");
                Indent();
                WriteEOL();
                WriteNode("DefaultBody", expr.DefaultBody, CreateMetadata(CSharpMultilineBlockTypes.Block));
                WriteStatementEnd(expr.DefaultBody);
                WriteEOL();
                Write("break;");
                Dedent();
            }

            WriteEOL(true);
            Write("}");
        }

        /// <summary>
        /// Schreibt ein catch
        /// </summary>
        /// <param name="catchBlock">die Anweisungen im Catch-Block</param>
        protected override void WriteCatchBlock(CatchBlock catchBlock)
        {
            Write("catch ");
            if (catchBlock.Variable != null || catchBlock.Test != typeof(Exception))
            {
                Write("(");
                if (catchBlock.Variable != null)
                    WriteNode("Variable", catchBlock.Variable, true);
                else
                    Write(catchBlock.Test.FriendlyName(language));
                Write(") ");
                if (catchBlock.Filter != null)
                {
                    Write("when (");
                    WriteNode("Filter", catchBlock.Filter, CreateMetadata(Test));
                    Write(") ");
                }
            }

            Write("{");
            Indent();
            WriteEOL();
            WriteNode("Body", catchBlock.Body, CreateMetadata(CSharpMultilineBlockTypes.Block));
            WriteStatementEnd(catchBlock.Body);
            WriteEOL(true);
            Write("}");
        }

        /// <summary>
        /// Schreibt ein try
        /// </summary>
        /// <param name="expr">die Anweisungen im try-Block</param>
        protected override void WriteTry(TryExpression expr)
        {
            Write("try {");
            Indent();
            WriteEOL();
            WriteNode("Body", expr.Body);
            WriteStatementEnd(expr.Body);
            WriteEOL(true);
            Write("}");
            expr.Handlers.ForEach((catchBlock, index) =>
            {
                Write(" ");
                WriteNode($"Handlers[{index}]", catchBlock);
            });
            if (expr.Fault != null)
            {
                Write(" fault {");
                Indent();
                WriteEOL();
                WriteNode("Fault", expr.Fault);
                WriteStatementEnd(expr.Fault);
                WriteEOL(true);
                Write("}");
            }

            if (expr.Finally != null)
            {
                Write(" finally {");
                Indent();
                WriteEOL();
                WriteNode("Finally", expr.Finally);
                WriteStatementEnd(expr.Finally);
                WriteEOL(true);
                Write("}");
            }
        }

        protected override void WriteLabel(LabelExpression expr)
        {
            WriteNode("Target", expr.Target);
            Write(":");
        }

        /// <summary>
        /// Schreibt ein goto-Statement
        /// </summary>
        /// <param name="expr">der Ausdruck</param>
        protected override void WriteGoto(GotoExpression expr)
        {
            var gotoKeyword = expr.Kind switch
            {
                Break => "break",
                Continue => "continue",
                GotoExpressionKind.Goto => "goto",
                GotoExpressionKind.Return => "return",
                _ => throw new NotImplementedException()
            };
            Write(gotoKeyword);
            if (!(expr.Target?.Name).IsNullOrWhitespace())
            {
                Write(" ");
                WriteNode("Target", expr.Target);
            }

            if (expr.Value != null)
            {
                Write(" ");
                WriteNode("Value", expr.Value);
            }
        }

        protected override void WriteLabelTarget(LabelTarget labelTarget)
        {
            Write(labelTarget.Name);
        }

        /// <summary>
        /// Schreibt eine Endlos-Schleife
        /// </summary>
        /// <param name="expr">der Ausdruck innerhalb der Schleife</param>
        protected override void WriteLoop(LoopExpression expr)
        {
            Write("while (true) {");
            Indent();
            WriteEOL();
            WriteNode("Body", expr.Body, CreateMetadata(CSharpMultilineBlockTypes.Block));
            WriteStatementEnd(expr.Body);
            WriteEOL(true);
            Write("}");
        }

        protected override void WriteRuntimeVariables(RuntimeVariablesExpression expr)
        {
            Write("// variables -- ");
            expr.Variables.ForEach((x, index) =>
            {
                if (index > 0) Write(", ");
                WriteNode($"Variables[{index}]", x, true);
            });
        }

        protected override void WriteDebugInfo(DebugInfoExpression expr)
        {
            var filename = expr.Document.FileName;
            Write("// ");
            var comment =
                expr.IsClear
                    ? $"Clear debug info from {filename}"
                    : $"Debug to {filename} -- L{expr.StartLine}C{expr.StartColumn} : L{expr.EndLine}C{expr.EndColumn}";
            Write(comment);
        }

        protected override void WriteBinaryOperationBinder(BinaryOperationBinder binder, IList<Expression> args)
        {
            VerifyCount(args, 2);
            WriteBinary(binder.Operation, "Arguments[0]", args[0], "Arguments[1]", args[1]);
        }

        protected override void WriteConvertBinder(ConvertBinder binder, IList<Expression> args)
        {
            VerifyCount(args, 1);
            WriteUnary(ExpressionType.Convert, "Arguments[0]", args[0], binder.Type, typeof(ConvertBinder).Name);
        }

        protected override void WriteCreateInstanceBinder(CreateInstanceBinder binder, IList<Expression> args)
        {
            WriteNew(binder.ReturnType, "Arguments", args);
        }

        protected override void WriteDeleteIndexBinder(DeleteIndexBinder binder, IList<Expression> args)
        {
            throw new NotImplementedException();
        }

        protected override void WriteDeleteMemberBinder(DeleteMemberBinder binder, IList<Expression> args)
        {
            throw new NotImplementedException();
        }

        protected override void WriteGetIndexBinder(GetIndexBinder binder, IList<Expression> args)
        {
            VerifyCount(args, 2, null);
            WriteNode("Arguments[0]", args[0]);
            Write("[");
            WriteNodes(args.Skip(1).Select((arg, index) => ($"Arguments[{index + 1}]", arg)));
            Write("]");
        }

        protected override void WriteGetMemberBinder(GetMemberBinder binder, IList<Expression> args)
        {
            VerifyCount(args, 1);
            WriteNode("Arguments[0]", args[0]);
            Write($".{binder.Name}");
        }

        protected override void WriteInvokeBinder(InvokeBinder binder, IList<Expression> args)
        {
            VerifyCount(args, 1, null);
            WriteNode("Arguments[0]", args[0]);
            Write("(");
            WriteNodes(args.Skip(1).Select((arg, index) => ($"Arguments[{index + 1}]", arg)));
            Write(")");
        }

        protected override void WriteInvokeMemberBinder(InvokeMemberBinder binder, IList<Expression> args)
        {
            VerifyCount(args, 1, null);
            WriteNode("Arguments[0]", args[0]);
            Write($".{binder.Name}(");
            WriteNodes(args.Skip(1).Select((arg, index) => ($"Arguments[{index + 1}]", arg)));
            Write(")");
        }

        protected override void WriteSetIndexBinder(SetIndexBinder binder, IList<Expression> args)
        {
            VerifyCount(args, 3, null);
            WriteNode("Arguments[0]", args[0]);
            Write("[");
            WriteNodes(args.Skip(2).Select((arg, index) => ($"Arguments[{index + 2}]", arg)));
            Write("] = ");
            WriteNode("Arguments[1]", args[1]);
        }

        protected override void WriteSetMemberBinder(SetMemberBinder binder, IList<Expression> args)
        {
            VerifyCount(args, 2);
            WriteNode("Arguments[0]", args[0]);
            Write($".{binder.Name} = ");
            WriteNode("Arguments[1]", args[1]);
        }

        protected override void WriteUnaryOperationBinder(UnaryOperationBinder binder, IList<Expression> args)
        {
            VerifyCount(args, 1);
            WriteUnary(binder.Operation, "Arguments[0]", args[0], binder.ReturnType, binder.GetType().Name);
        }

        /// <summary>
        /// Adds a bind variable and writes the according parameter
        /// </summary>
        /// <param name="bindValue">Value the parameter is bound to</param>
        private void AddAndWriteBindVar(object bindValue)
        {
            var parameter = "p";

            if (!BindVars.ContainsKey(parameter))
            {
                BindVars.Add(parameter, bindValue);
            }
            else
            {
                //generate a unique GUID and get rid of the "-" so that it can be added to the name of the parameter
                var unique = Guid.NewGuid().ToString().Replace("-", "");
                parameter += unique;
                BindVars.Add(parameter, bindValue);
            }

            Write("@" + parameter);
        }
    }
}