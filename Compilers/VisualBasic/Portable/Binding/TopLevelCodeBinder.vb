' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports Microsoft.CodeAnalysis.VisualBasic.Symbols

Namespace Microsoft.CodeAnalysis.VisualBasic

    ''' <summary>
    ''' Provides context for binding top-level statements in a script. 
    ''' </summary>
    Friend NotInheritable Class TopLevelCodeBinder
        Inherits SubOrFunctionBodyBinder

        Private ReadOnly _scriptInitializer As SynthesizedInteractiveInitializerMethod

        ''' <summary>
        ''' Create binder for binding the body of a method.
        ''' The root is the tree that contains the top-level code being bound; a script class may span multiple trees.
        ''' </summary>
        Public Sub New(scriptInitializer As MethodSymbol, root As SyntaxNode, containingBinder As Binder)
            MyBase.New(scriptInitializer, root, containingBinder)
            Debug.Assert(scriptInitializer.ContainingType.IsScriptClass)
            _scriptInitializer = TryCast(scriptInitializer, SynthesizedInteractiveInitializerMethod)
        End Sub

        Public Overrides Function GetLocalForFunctionValue() As LocalSymbol
            Return _scriptInitializer?.FunctionLocal
        End Function

        Public Overrides Function GetReturnLabel() As LabelSymbol
            Return If(_scriptInitializer IsNot Nothing, _scriptInitializer.ExitLabel, MyBase.GetReturnLabel())
        End Function

        Public Overrides ReadOnly Property IsInQuery As Boolean
            Get
                Return False
            End Get
        End Property
    End Class

End Namespace
