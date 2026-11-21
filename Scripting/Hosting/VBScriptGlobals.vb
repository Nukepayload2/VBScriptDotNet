' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Collections.Generic
Imports Microsoft.CodeAnalysis.Scripting.Hosting
Imports Microsoft.CodeAnalysis.VisualBasic.Scripting.Hosting

Namespace Nukepayload2.VBScriptDotNet.Hosting

    ''' <summary>
    ''' Members scripts written against vbi expect: <c>Print</c> and <c>Args</c>. Hosts that need different output
    ''' derive from this class (and then pass the subclass as <see cref="VBScriptOptions.Globals"/> or
    ''' <see cref="VBScriptOptions.GlobalsType"/>, which must derive from this type).
    ''' </summary>
    Public Class VBScriptGlobals

        Private ReadOnly _output As IScriptOutput
        Private ReadOnly _objectFormatter As ObjectFormatter
        Private ReadOnly _printOptions As PrintOptions

        Public Sub New()
            Me.New(NullScriptOutput.Instance)
        End Sub

        Public Sub New(output As IScriptOutput)
            If output Is Nothing Then
                Throw New ArgumentNullException(NameOf(output))
            End If

            _output = output
            _objectFormatter = VisualBasicObjectFormatter.Instance
            _printOptions = New PrintOptions()
            Args = New List(Of String)()
        End Sub

        ''' <summary>Arguments the host passed for this execution (<see cref="VBScriptOptions.ScriptArguments"/>).</summary>
        Public ReadOnly Property Args As IList(Of String)

        ''' <summary>Pretty-prints a value, using the same formatter vbi uses.</summary>
        Public Overridable Sub Print(value As Object)
            _output.Out.WriteLine(_objectFormatter.FormatObject(value, _printOptions))
        End Sub

    End Class

End Namespace
