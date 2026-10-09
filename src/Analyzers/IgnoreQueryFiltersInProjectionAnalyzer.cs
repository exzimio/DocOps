using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DocOps.Analyzers;

/// <summary>
/// Recusa <c>IgnoreQueryFilters()</c> escrito dentro de uma projeção.
///
/// É um operador de <b>nível de consulta</b>, não de subconsulta. Escrito dentro do
/// <c>Select</c> de outra consulta — uma contagem, por exemplo — não desliga os filtros dessa
/// contagem: desliga-os da consulta <b>inteira</b> a que pertence, incluindo o filtro de organização
/// da de fora.
///
/// Nada falha. Nenhuma exceção, nenhum aviso, nenhum teste vermelho. A lista simplesmente passa a
/// incluir linhas de outra organização, e só quem entrasse como a outra organização daria por isso.
///
/// Usar o operador no topo de uma consulta é legítimo e frequente aqui: a semeadura corre antes de
/// existir organização, o worker corre acima de todas, e às vezes é preciso reler de propósito uma
/// linha removida. Esses usos são visíveis e deliberados. O que isto proíbe é o invisível — o
/// operador enterrado numa projeção, onde o seu alcance verdadeiro não é o que parece.
///
/// <para>
/// <b>Isto foi um teste que lia o código-fonte como texto, e contava parênteses para adivinhar se
/// estava dentro de um <c>Select</c>.</b> Funcionava, mas confundia-se com comentários e com
/// literais de texto, dependia da formatação, e não sabia se aquele <c>IgnoreQueryFilters</c> era
/// sequer o do Entity Framework. Aqui a pergunta é feita ao compilador: o símbolo é resolvido pelo
/// modelo semântico, e a posição na árvore sintática é um facto, não uma contagem.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class IgnoreQueryFiltersInProjectionAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "DOCOPS001";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "IgnoreQueryFilters() dentro de uma projeção desliga os filtros da consulta inteira",
        messageFormat:
            "«IgnoreQueryFilters()» aqui desliga os filtros da consulta inteira, incluindo o da "
            + "organização. Mova a subconsulta para uma consulta própria e escreva a condição de "
            + "organização à mão.",
        category: "DocOps.MultiTenancy",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "Operador de nível de consulta. Dentro de uma projeção não limita o seu efeito à "
            + "subconsulta: alcança a consulta exterior e o seu filtro de organização.");

    /// <summary>Os operadores cujo argumento é uma projeção ou uma condição sobre cada linha.</summary>
    private static readonly ImmutableHashSet<string> ProjectionOperators = ImmutableHashSet.Create(
        "Select", "SelectMany", "Where", "Any", "All", "Count", "LongCount", "Sum", "Average",
        "Min", "Max", "First", "FirstOrDefault", "Single", "SingleOrDefault", "OrderBy",
        "OrderByDescending", "ThenBy", "ThenByDescending", "GroupBy");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(Analyse, SyntaxKind.InvocationExpression);
    }

    private static void Analyse(SyntaxNodeAnalysisContext context)
    {
        InvocationExpressionSyntax invocation = (InvocationExpressionSyntax)context.Node;

        if (invocation.Expression is not MemberAccessExpressionSyntax member
            || member.Name.Identifier.ValueText != "IgnoreQueryFilters")
        {
            return;
        }

        // Perguntar ao compilador se é mesmo o do Entity Framework, em vez de acreditar no nome.
        // Um método homónimo noutra classe não tem nada que ver com isto.
        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol
            is not IMethodSymbol symbol
            || symbol.ContainingType?.Name != "EntityFrameworkQueryableExtensions")
        {
            return;
        }

        if (EnclosingProjection(invocation) is { } projection)
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, member.Name.GetLocation(), projection));
        }
    }

    /// <summary>
    /// Sobe a árvore à procura de uma lambda que seja argumento de um operador de projeção.
    ///
    /// Subir é o que distingue este analisador da contagem de parênteses que existia antes: a
    /// relação «esta chamada está dentro do argumento daquele operador» é uma propriedade da
    /// estrutura do programa, e o compilador já a tem construída.
    /// </summary>
    private static string? EnclosingProjection(SyntaxNode node)
    {
        for (SyntaxNode? current = node.Parent; current is not null; current = current.Parent)
        {
            // Uma consulta nova começa aqui: o que estiver dentro de um corpo de método ou de uma
            // função local pertence a essa consulta, e não à de fora.
            if (current is MethodDeclarationSyntax or LocalFunctionStatementSyntax)
            {
                return null;
            }

            if (current is not LambdaExpressionSyntax lambda)
            {
                continue;
            }

            if (lambda.Parent is ArgumentSyntax { Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax outer } }
                && outer.Expression is MemberAccessExpressionSyntax outerMember
                && ProjectionOperators.Contains(outerMember.Name.Identifier.ValueText))
            {
                return outerMember.Name.Identifier.ValueText;
            }
        }

        return null;
    }
}
