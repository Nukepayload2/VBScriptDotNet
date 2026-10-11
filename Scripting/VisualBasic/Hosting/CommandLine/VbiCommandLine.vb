' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

' Host-level command line shapes accepted by vbi but not by the compiler option table.

Imports System

Namespace Microsoft.CodeAnalysis.VisualBasic.Scripting.Hosting

    Friend NotInheritable Class VbiCommandLine

        Private Sub New()
        End Sub

        ''' <summary>
        ''' Folds a GNU-style double dash switch ("--version") into the form the compiler option table
        ''' understands ("/version"). Everything else, including a bare "--", is returned unchanged.
        ''' </summary>
        ''' <remarks>
        ''' CommandLineParser.TryParseOption strips exactly one leading '/' or '-' from an argument
        ''' (Compilers\Core\Portable\CommandLine\CommandLineParser.cs:200-210), so "--version" is looked up
        ''' under the name "-version", misses the option table and is reported as an unrecognized option
        ''' (BC2007) - after which the host silently falls back to the REPL. F# Interactive accepts
        ''' "--version" and "--help", so the extra dash is folded here, at the host boundary, rather than in
        ''' the shared parser (which also serves vbc/csc and must not accept what those compilers reject).
        ''' A bare "--" separates script arguments from options: it and every argument after it are left
        ''' alone, matching the "vbi [options] [script-file.vbx] [-- script-arguments]" usage.
        ''' </remarks>
        Friend Shared Function NormalizeLongSwitches(args As String()) As String()
            If args Is Nothing OrElse args.Length = 0 Then
                Return Array.Empty(Of String)()
            End If

            Dim separator = Array.IndexOf(args, "--")
            Dim limit = If(separator < 0, args.Length, separator)
            Dim result = DirectCast(args.Clone(), String())

            For i = 0 To limit - 1
                Dim arg = args(i)
                ' Length > 2 keeps the stdin switch "-" and the bare "--" separator out of the fold.
                If arg IsNot Nothing AndAlso arg.Length > 2 AndAlso arg(0) = "-"c AndAlso arg(1) = "-"c Then
                    result(i) = "/" & arg.Substring(2)
                End If
            Next

            Return result
        End Function

    End Class

End Namespace
