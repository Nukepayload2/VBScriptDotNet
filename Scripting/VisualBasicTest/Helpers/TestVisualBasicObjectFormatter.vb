' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Globalization
Imports Microsoft.CodeAnalysis.Scripting.Hosting
Imports Microsoft.CodeAnalysis.VisualBasic.Scripting.Hosting

Friend NotInheritable Class TestVisualBasicObjectFormatter
    Inherits VisualBasicObjectFormatterImpl

    Private ReadOnly _quoteStringsAndCharacters As Boolean
    Private ReadOnly _maximumLineLength As Integer
    Private ReadOnly _cultureInfo As CultureInfo

    Public Sub New(Optional quoteStringsAndCharacters As Boolean = True,
                   Optional maximumLineLength As Integer = Integer.MaxValue,
                   Optional cultureInfo As CultureInfo = Nothing)
        _quoteStringsAndCharacters = quoteStringsAndCharacters
        _maximumLineLength = maximumLineLength
        _cultureInfo = If(cultureInfo, CultureInfo.InvariantCulture)
    End Sub

    Protected Overrides Function GetInternalBuilderOptions(printOptions As PrintOptions) As BuilderOptions
        Return New BuilderOptions(indentation:="  ",
                                  newLine:=Environment.NewLine,
                                  ellipsis:=printOptions.Ellipsis,
                                  maximumLineLength:=_maximumLineLength,
                                  maximumOutputLength:=printOptions.MaximumOutputLength)
    End Function

    Protected Overrides Function GetPrimitiveOptions(printOptions As PrintOptions) As CommonPrimitiveFormatterOptions
        Return New CommonPrimitiveFormatterOptions(numberRadix:=printOptions.NumberRadix,
                                                   includeCodePoints:=False,
                                                   escapeNonPrintableCharacters:=printOptions.EscapeNonPrintableCharacters,
                                                   quoteStringsAndCharacters:=_quoteStringsAndCharacters,
                                                   cultureInfo:=_cultureInfo)
    End Function
End Class