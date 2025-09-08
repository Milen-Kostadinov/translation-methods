using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using static scsc.Parser;

namespace scsc
{
    public class MyParser
    {
        private MyScanner scanner;
        private Emit emit;
        private Table symbolTable;
        private Token token;
        private Diagnostics diag;

        private Stack<Label> breakStack = new Stack<Label>();
        private Stack<Label> continueStack = new Stack<Label>();

        public MyParser(MyScanner scanner, Emit emit, Table symbolTable, Diagnostics diag)
        {
            this.scanner = scanner;
            this.emit = emit;
            this.symbolTable = symbolTable;
            this.diag = diag;
        }

        public void AddPredefinedSymbols()
        {
            symbolTable.AddToUniverse(new PrimitiveTypeSymbol(new IdentToken(-1, -1, "int"), typeof(System.Int32)));
            symbolTable.AddToUniverse(new PrimitiveTypeSymbol(new IdentToken(-1, -1, "bool"), typeof(System.Boolean)));
            symbolTable.AddToUniverse(new PrimitiveTypeSymbol(new IdentToken(-1, -1, "char"), typeof(System.Char)));
            symbolTable.AddToUniverse(new PrimitiveTypeSymbol(new IdentToken(-1, -1, "*"), typeof(System.TypedReference)));
            symbolTable.AddToUniverse(new PrimitiveTypeSymbol(new IdentToken(-1, -1, "pchar"), typeof(System.String)));
        }

        public bool Parse()
        {
            ReadNextToken();
            AddPredefinedSymbols();
            return IsProgram() && token is EOFToken;
        }

        public void ReadNextToken()
        {
            token = scanner.Next();
        }

        public bool CheckKeyword(string keyword)
        {
            bool result = (token is KeywordToken) && ((KeywordToken)token).value == keyword;
            if (result) ReadNextToken();
            return result;
        }

        public bool CheckSpecialSymbol(string symbol)
        {
            bool result = (token is SpecialSymbolToken) && ((SpecialSymbolToken)token).value == symbol;
            if (result) ReadNextToken();
            return result;
        }

        public bool CheckIdent()
        {
            bool result = (token is IdentToken);
            if (result) ReadNextToken();
            return result;
        }

        void SkipUntilSemiColon()
        {
            Token Tok;
            do
            {
                Tok = scanner.Next();
            } while (!((Tok is EOFToken) ||
                         (Tok is SpecialSymbolToken) && ((Tok as SpecialSymbolToken).value == ";")));
        }

        public void Error(string message)
        {
            diag.Error(token.line, token.column, message);
            SkipUntilSemiColon();
        }

        public void Error(string message, Token token)
        {
            diag.Error(token.line, token.column, message);
            SkipUntilSemiColon();
        }

        public void Error(string message, Token token, params object[] par)
        {
            diag.Error(token.line, token.column, string.Format(message, par));
            SkipUntilSemiColon();
        }
        public bool AssignableTypes(Type typeAssignTo, Type typeAssignFrom)
        {
            //return typeAssignTo==typeAssignFrom;
            return typeAssignTo.IsAssignableFrom(typeAssignFrom);
        }

        public bool IsProgram()
        {
            emit.InitProgramClass("MyProgramme");
            while (IsStatement() && !(token is EOFToken)) ;
            return diag.GetErrorCount() == 0;
        }
        public bool IsStatement()
        {
            Type t;
            bool isStatement = IsCompoundStatement()
                || IsIfStatement()
                || IsWhileStatement()
                || (IsExpression(out t))
                || CheckSpecialSymbol(";")
                || IsPredefinedCommand()
                || IsBugStatement();
            return isStatement;
        }
        public void Warning(string message)
        {
            diag.Warning(token.line, token.column, message);
        }
        public bool IsBugStatement()
        {
            if (CheckKeyword("bug"))
            {
                if (!IsStatement())
                    Error("Очкавам твърдение!");

                ReadNextToken();

                if (!CheckSpecialSymbol(";"))
                    Error("Очаквам ключов символ ';'!");

                Warning("Твърдението може да съдържа бъг!");

                return true;
            }
            return false;
        }
        public bool IsPredefinedCommand()
        {
            String tokenValue = "Yes";
            if (CheckKeyword("printf"))
            {
                if (!CheckSpecialSymbol("(")) Error("Очаквам специален символ ( ");
                var id = token as IdentToken;
                var location = new LocationInfo();
                location.id = symbolTable.GetSymbol(id.value);
                if ((token is IdentToken) && symbolTable.ExistCurrentScopeSymbol((token as IdentToken).value))
                    tokenValue = (token as IdentToken).value;
                ReadNextToken();
                if (!CheckSpecialSymbol(")")) Error("Очаквам специален символ ) ");
                if (!CheckSpecialSymbol(";")) Error("Очаквам специален символ ; ");
                Console.WriteLine(symbolTable.GetSymbol(id.value)); 
                return true;
            } 
            return false;
        }
        public bool IsCompoundStatement()
        {
            Token tokes = token;
            if (!CheckSpecialSymbol("{"))
                return false;

            while (IsDeclaration() || IsStatement() || IsPredefinedCommand()) ;
            if (!CheckSpecialSymbol("}")) Error("Очаквам специален знак '}' !");
            return true;
        }
        public bool IsIfStatement()
        {
            Type type;
            if (CheckKeyword("if"))
            {
                // 'if' '(' Expression ')' Statement ['else' Statement]
                if (!CheckSpecialSymbol("(")) Error("Очаквам специален символ '('");
                if (!IsExpression(out type)) Error("Очаквам израз");
                if (!AssignableTypes(typeof(System.Boolean), type)) Error("Типа на изразът трябва да е Boolean");
                if (!CheckSpecialSymbol(")")) Error("Очаквам специален символ ')'");

                // Emit
                Label labelElse = emit.GetLabel();
                emit.AddCondBranch(labelElse);

                if (!IsStatement()) Error("Очаквам Statement");
                if (CheckKeyword("else"))
                {
                    // Emit
                    Label labelEnd = emit.GetLabel();
                    emit.AddBranch(labelEnd);
                    emit.MarkLabel(labelElse);

                    if (!IsStatement()) Error("Очаквам Statement");

                    // Emit
                    emit.MarkLabel(labelEnd);
                }
                else
                {
                    // Emit
                    emit.MarkLabel(labelElse);
                }
                return true;
            }
            return false;
        }
        public bool IsWhileStatement()
        {
            if (CheckKeyword("while"))
            {
                Type type;
                // Emit
                Label labelContinue = emit.GetLabel();
                Label labelBreak = emit.GetLabel();
                breakStack.Push(labelBreak);
                continueStack.Push(labelContinue);

                emit.MarkLabel(labelContinue);

                if (!CheckSpecialSymbol("(")) Error("Очаквам специален символ '('");
                if (!IsExpression(out type)) Error("Очаквам израз");
                if (!AssignableTypes(typeof(System.Boolean), type)) Error("Типа на изразът трябва да е Boolean");
                if (!CheckSpecialSymbol(")")) Error("Очаквам специален символ ')'");

                // Emit
                emit.AddCondBranch(labelBreak);

                if (!IsStatement()) Error("Очаквам Statement");

                // Emit
                emit.AddBranch(labelContinue);
                emit.MarkLabel(labelBreak);

                breakStack.Pop();
                continueStack.Pop();
            }
            return false;
        }
        public bool IsStopStatement()
        {
            Type type;
            if (CheckKeyword("return"))
            {
                Type retType = emit.GetMethodReturnType();
                if (retType != typeof(void))
                {
                    IsExpression(out type);
                    if (!AssignableTypes(retType, type)) Error("Типа на резултата трябва да е съвместим с типа на метода");
                }
                if (!CheckSpecialSymbol(";")) Error("Очаквам специален символ ';'");

                // Emit
                emit.AddReturn();

            }
            else if (CheckKeyword("break"))
            {
                if (!CheckSpecialSymbol(";")) Error("Очаквам специален символ ';'");

                // Emit
                emit.AddBranch((Label)breakStack.Peek());

            }
            else if (CheckKeyword("continue"))
            {
                if (!CheckSpecialSymbol(";")) Error("Очаквам специален символ ';'");

                // Emit
                emit.AddBranch((Label)continueStack.Peek());

            }
            return false;
        }
        public bool IsDeclaration()
        {
            if (!IsVarDefOrMethod()) return false;
            return true;
        }
        public bool IsVarDefOrMethod()
        {
            IdentToken name;
            IdentToken paramName;
            List<FormalParamSymbol> formalParams = new List<FormalParamSymbol>();
            List<Type> formalParamTypes = new List<Type>();
            Type paramType;
            Type type;

            if (!IsType(out type)) return false;
            name = token as IdentToken;
            if (!CheckIdent()) Error("Очаквам идентификатор");
            if (CheckSpecialSymbol("("))
            {
                // Семантична грешка - редеклариран ли е методът повторно?
                if (symbolTable.ExistCurrentScopeSymbol(name.value)) Error($"Метода {0} е редеклариран", name, name.value);
                // Emit
                MethodSymbol methodToken = symbolTable.AddMethod(name, type, formalParams.ToArray(), null);
                symbolTable.BeginScope();

                while (IsType(out paramType))
                {
                    paramName = token as IdentToken;
                    if (!CheckIdent()) Error("Очаквам идентификатор");
                    // Семантична грешка - редеклариран ли е формалният параметър повторно?
                    if (symbolTable.ExistCurrentScopeSymbol(paramName.value)) Error($"Формалния параметър {0} е редеклариран", paramName, paramName.value);
                    FormalParamSymbol formalParam = symbolTable.AddFormalParam(paramName, paramType, null);
                    formalParams.Add(formalParam);
                    formalParamTypes.Add(paramType);
                    if (!CheckSpecialSymbol(",")) break;
                }
                if (!CheckSpecialSymbol(")")) Error("Очаквам специален символ ')'");

                methodToken.methodInfo = emit.AddMethod(name.value, type, formalParamTypes.ToArray());
                for (int i = 0; i < formalParams.Count; i++)
                {
                    formalParams[i].parameterInfo = emit.AddParam(formalParams[i].value, i + 1, formalParamTypes[i]);
                }
                methodToken.formalParams = formalParams.ToArray();

                if (!IsCompoundStatement()) Error("Очаквам блок");

                symbolTable.EndScope();

                return true;
            }

            if (!CheckSpecialSymbol(";")) Error("Очаквам специален символ ';'");

            // Семантична грешка - редекларирано ли е полето повторно?
            if (symbolTable.ExistCurrentScopeSymbol(name.value)) Error($"Полето {0} е редекларирано", name, name.value);
            if (type == typeof(void)) Error($"Полето {0} не може да е от тип void", name, name.value);

            // Emit (field)
            symbolTable.AddLocalVar(name, emit.AddLocalVar(name.value, type));

            return true;
        }
        public bool IsType(out Type type)
        {
            type = null;
            if (CheckKeyword("int"))
                type = typeof(System.Int32);
            if (CheckKeyword("bool"))
                type = typeof(System.Boolean);
            if (CheckKeyword("*"))
                type = typeof(System.Char);
            if (CheckKeyword("string"))
                type = typeof(System.String);
            if (type != null)
            {
                return true;
            }
            return false;
        }
        public bool IsExpression(out Type type)
        {
            if (!IsAdditiveExpression(out type)) return false;
            SpecialSymbolToken opToken = token as SpecialSymbolToken;
            if (CheckSpecialSymbol("<") || CheckSpecialSymbol("<=") || CheckSpecialSymbol("==") || CheckSpecialSymbol("!=") || CheckSpecialSymbol(">=") || CheckSpecialSymbol(">"))
            {
                Type type1;
                if (!IsAdditiveExpression(out type1)) Error("Очаквам адитивен израз");
                if (type != type1) Error("Несъвместими типове за сравнение");

                //Emit
                emit.AddConditionOp(opToken.value);

                type = typeof(System.Boolean);

            }

            return true;
        }
        public bool IsAdditiveExpression(out Type type)
        {
            SpecialSymbolToken opToken = token as SpecialSymbolToken;
            bool unaryMinus = false;
            bool unaryOp = false;
            if (CheckSpecialSymbol("+") || CheckSpecialSymbol("-"))
            {
                unaryMinus = ((SpecialSymbolToken)token).value == "-";
                unaryOp = true;
            }
            if (!IsMultiplicativeExpression(out type))
            {
                if (unaryOp) Error("Очаквам мултипликативен израз");
                else return false;
            }

            // Emit
            if (unaryMinus)
            {
                emit.AddUnaryOp("-");
            }

            opToken = token as SpecialSymbolToken;
            while (CheckSpecialSymbol("+") || CheckSpecialSymbol("-") || CheckSpecialSymbol("|") || CheckSpecialSymbol("||") || CheckSpecialSymbol("^"))
            {
                Type type1;
                if (!IsMultiplicativeExpression(out type1)) Error("Очаквам мултипликативен израз");

                // Types check
                if (opToken.value == "||")
                {
                    if (type == typeof(System.Boolean) && type1 == typeof(System.Boolean))
                    {
                        ;
                    }
                    else
                    {
                        Error("Несъвместими типове", opToken);
                    }
                }
                else
                {
                    Error("Несъвместими типове");
                }

                //Emit
                if (opToken.value == "+" && type == typeof(System.String))
                {
                    emit.AddConcatinationOp();
                }
                else
                {
                    emit.AddAdditiveOp(opToken.value);
                }

                opToken = token as SpecialSymbolToken;
            }

            return true;
        }
        public bool IsMultiplicativeExpression(out Type type)
        {
            if (!IsSimpleExpression(out type)) return false;

            SpecialSymbolToken opToken = token as SpecialSymbolToken;
            while (CheckSpecialSymbol("*") || CheckSpecialSymbol("/") || CheckSpecialSymbol("%") || CheckSpecialSymbol("&") || CheckSpecialSymbol("&&"))
            {
                Type type1;
                if (!IsSimpleExpression(out type1)) Error("Очаквам прост израз");

                // Types check
                if (opToken.value == "&&")
                {
                    if (type == typeof(System.Boolean) && type1 == typeof(System.Boolean))
                    {
                        ;
                    }
                    else
                    {
                        Error("Несъвместими типове");
                    }
                }
                else
                {
                    Error("Несъвместими типове");
                }

                //Emit
                emit.AddMultiplicativeOp(opToken.value);

                opToken = token as SpecialSymbolToken;
            }

            return true;
        }
        public enum IncDecOps { None, PreInc, PreDec, PostInc, PostDec }
        public bool IsSimpleExpression(out Type type)
        {
            if(IsPrimaryExpression(out type))
                return true;
            return false;
        }
        public bool IsPrimaryExpression(out Type type)
        {
            if (IsConstant(out type)) return true;
            if (!(token is IdentToken))
            {
                type = null;
                return false;
            }
            var id = token as IdentToken;
            var location = new LocationInfo();
            location.id = symbolTable.GetSymbol(id.value);
            // Семантична грешка - деклариран ли е вече идентификатора?
            if (location.id == null) Error($"Недеклариран идентификатор {id} {id.value}", id, id.value);
            LocalVarSymbol lvs = location.id as LocalVarSymbol;
            if (lvs != null)
            {
                Token lastToken = token;
                ReadNextToken();
                if (CheckSpecialSymbol("="))
                {
                    if (!IsExpression(out type)) Error("Очаквам израз");
                    ReadNextToken();
                    if (!CheckSpecialSymbol(";")) Error("Очаквам специален символ ';'");

                    // Emit
                    if (!AssignableTypes(lvs.localVariableInfo.LocalType, type)) Error("Несъвместими типове", location.id);
                    emit.AddAssignCast(lvs.localVariableInfo.LocalType, type);
                    emit.AddLocalVarAssigment(lvs.localVariableInfo);

                    return true;
                }
                else
                {
                    if (lastToken is IdentToken && symbolTable.ExistCurrentScopeSymbol((lastToken as IdentToken).value))
                        type = typeof(System.Boolean);
                    return true;
                }
            }
            type = null;
            return false;
        }
        public bool IsConstant(out Type type)
        {
            type = null;
            if (token is NumberToken) type = typeof(System.Int32);
            if (token is BooleanToken) type = typeof(System.Boolean);
            if (type != null) return true;
            return false;
        }
    }
}
