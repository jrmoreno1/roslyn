﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Immutable;
using System.Composition;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Extensions;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Microsoft.CodeAnalysis.Simplification;

namespace Microsoft.CodeAnalysis.CSharp.CodeFixes.UseImplicitTyping
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = PredefinedCodeFixProviderNames.UseExplicitTyping), Shared]
    internal class UseExplicitTypingCodeFixProvider : CodeFixProvider
    {
        public override ImmutableArray<string> FixableDiagnosticIds =>
            ImmutableArray.Create(IDEDiagnosticIds.UseExplicitTypingDiagnosticId);

        public override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var document = context.Document;
            var span = context.Span;
            var root = await document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);

            var token = root.FindToken(span.Start);
            if (!token.Span.IntersectsWith(span))
            {
                return;
            }

            var node = root.FindNode(span, getInnermostNodeForTie: true);

            var codeAction = new MyCodeAction(
                CSharpFeaturesResources.UseExplicitType,
                c => HandleDeclaration(document, root, node, context.CancellationToken));

            context.RegisterCodeFix(codeAction, context.Diagnostics.First());
        }

        private async Task<Document> HandleDeclaration(Document document, SyntaxNode root, SyntaxNode node, CancellationToken cancellationToken)
        {
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            var declarationContext = node.Parent;

            TypeSyntax typeSyntax;
            if (declarationContext is VariableDeclarationSyntax)
            {
                typeSyntax = ((VariableDeclarationSyntax)declarationContext).Type;
            }
            else if (declarationContext is ForEachStatementSyntax)
            {
                typeSyntax = ((ForEachStatementSyntax)declarationContext).Type;
            }
            else
            {
                Debug.Assert(false, $"unhandled kind {declarationContext.Kind().ToString()}");
                return document;
            }

            var typeSymbol = semanticModel.GetTypeInfo(typeSyntax).ConvertedType;

            var typeName = SyntaxFactory.ParseTypeName(typeSymbol.ToDisplayString())
                                            .WithLeadingTrivia(node.GetLeadingTrivia())
                                            .WithTrailingTrivia(node.GetTrailingTrivia())
                                            .WithAdditionalAnnotations(Simplifier.Annotation);

            Debug.Assert(!typeName.ContainsDiagnostics, "Explicit type replacement likely introduced an error in code");

            var newRoot = root.ReplaceNode(node, typeName);
            return document.WithSyntaxRoot(newRoot);
        }

        private class MyCodeAction : CodeAction.DocumentChangeAction
        {
            public MyCodeAction(string title, Func<CancellationToken, Task<Document>> createChangedDocument) :
                base(title, createChangedDocument)
            {
            }
        }
    }
}