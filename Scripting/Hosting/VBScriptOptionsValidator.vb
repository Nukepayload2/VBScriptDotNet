' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Collections.Generic
Imports System.IO
Imports Microsoft.Extensions.Options

Namespace Nukepayload2.VBScriptDotNet.Hosting

    ''' <summary>
    ''' Validates <see cref="VBScriptOptions"/> at options-resolution time (and at host start when the
    ''' <c>IHostApplicationBuilder</c> overload of <c>AddVBScripting</c> was used): a globals type that does not
    ''' derive from <see cref="VBScriptGlobals"/>, a working directory that does not exist, and blank entries in
    ''' <see cref="VBScriptOptions.Imports"/> or <see cref="VBScriptOptions.References"/> are configuration errors,
    ''' not script failures, so they fail fast instead of surfacing as a confusing compile error later.
    ''' </summary>
    Friend NotInheritable Class VBScriptOptionsValidator
        Implements IValidateOptions(Of VBScriptOptions)

        Public Function Validate(name As String, options As VBScriptOptions) As ValidateOptionsResult Implements IValidateOptions(Of VBScriptOptions).Validate
            If options Is Nothing Then
                Return ValidateOptionsResult.Success
            End If

            Dim failures As New List(Of String)()

            If options.GlobalsType IsNot Nothing AndAlso Not GetType(VBScriptGlobals).IsAssignableFrom(options.GlobalsType) Then
                failures.Add(
                    "VBScriptOptions.GlobalsType must derive from " & GetType(VBScriptGlobals).FullName &
                    ", but '" & options.GlobalsType.FullName & "' does not. Derive the type from VBScriptGlobals, " &
                    "or set VBScriptOptions.Globals to an instance instead.")
            End If

            If Not String.IsNullOrWhiteSpace(options.WorkingDirectory) AndAlso Not Directory.Exists(options.WorkingDirectory) Then
                failures.Add(
                    "VBScriptOptions.WorkingDirectory '" & options.WorkingDirectory & "' does not exist. Set it to " &
                    "an existing directory, or leave it empty to use the process current directory.")
            End If

            If ContainsBlankEntry(options.[Imports]) Then
                failures.Add(
                    "VBScriptOptions.Imports contains an empty entry. Remove it; every import must name a namespace.")
            End If

            If ContainsBlankEntry(options.References) Then
                failures.Add(
                    "VBScriptOptions.References contains an empty entry. Remove it; every reference must name an " &
                    "assembly or an assembly path.")
            End If

            If failures.Count = 0 Then
                Return ValidateOptionsResult.Success
            End If

            Return ValidateOptionsResult.Fail(failures)
        End Function

        Private Shared Function ContainsBlankEntry(items As IList(Of String)) As Boolean
            If items Is Nothing Then
                Return False
            End If

            For Each item In items
                If String.IsNullOrWhiteSpace(item) Then
                    Return True
                End If
            Next

            Return False
        End Function

    End Class

End Namespace
