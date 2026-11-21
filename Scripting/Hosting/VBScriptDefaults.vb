' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Collections.Generic
Imports System.Collections.Immutable
Imports System.IO
Imports System.Reflection
Imports System.Text.RegularExpressions

Namespace Nukepayload2.VBScriptDotNet.Hosting

    ''' <summary>
    ''' The default references/imports, parsed from the <c>vbi.coreclr.rsp</c> copy embedded in this assembly.
    ''' Shipping the file inside the package keeps the default set from drifting away from vbi's defaults: the unit
    ''' tests compare both item by item against the repository file.
    ''' </summary>
    Friend NotInheritable Class VBScriptDefaults

        Private Const ResourceName As String = "Nukepayload2.VBScriptDotNet.Hosting.vbi.coreclr.rsp"

        Private Shared ReadOnly s_referenceLine As New Regex("^/r:(.+)$", RegexOptions.CultureInvariant)
        Private Shared ReadOnly s_importLine As New Regex("^/imports:(.+)$", RegexOptions.CultureInvariant)
        Private Shared ReadOnly s_lazyParsed As New Lazy(Of ParsedDefaults)(AddressOf ParseEmbeddedResponseFile)

        Private Sub New()
        End Sub

        ''' <summary>The <c>/r:</c> items of <c>vbi.coreclr.rsp</c>, in file order.</summary>
        Friend Shared ReadOnly Property DefaultReferences As ImmutableArray(Of String)
            Get
                Return s_lazyParsed.Value.References
            End Get
        End Property

        ''' <summary>The <c>/imports:</c> items of <c>vbi.coreclr.rsp</c>, in file order.</summary>
        Friend Shared ReadOnly Property DefaultImports As ImmutableArray(Of String)
            Get
                Return s_lazyParsed.Value.[Imports]
            End Get
        End Property

        ''' <summary>The desktop references vbi's <c>vbi.desktop.rsp</c> adds. Opt-in, net472 only.</summary>
        Friend Shared ReadOnly Property DesktopReferences As ImmutableArray(Of String) =
            ImmutableArray.Create("System.Drawing", "System.Windows.Forms")

        ''' <summary>
        ''' False on net10: the desktop reference surface is not part of the default set there, so
        ''' <see cref="VBScriptOptions.UseDesktopReferences"/> is ignored. The DI runner logs one warning
        ''' for that case; this layer has no logger, so it only ignores the flag.
        ''' </summary>
        Friend Shared ReadOnly Property SupportsDesktopReferences As Boolean =
            IsDesktopReferencePlatform()

        Private Shared Function IsDesktopReferencePlatform() As Boolean
#If NETFRAMEWORK Then
            Return True
#Else
            Return False
#End If
        End Function

        ''' <summary>
        ''' Merges the vbi default set into <paramref name="options"/>: host entries keep their position and are
        ''' never replaced, defaults are appended only when missing, and duplicates are dropped
        ''' case-insensitively in both lists.
        ''' <para>
        ''' Blank entries are deliberately preserved: repairing host input silently would make the options
        ''' validator's "no blank entries" check unreachable. The validator reports them instead.
        ''' </para>
        ''' </summary>
        Friend Shared Sub ApplyDefaults(options As VBScriptOptions)
            If options Is Nothing Then
                Throw New ArgumentNullException(NameOf(options))
            End If

            If options.[Imports] Is Nothing Then
                options.[Imports] = New List(Of String)()
            End If

            If options.References Is Nothing Then
                options.References = New List(Of String)()
            End If

            Merge(options.[Imports], DefaultImports)
            Merge(options.References, DefaultReferences)

            If options.UseDesktopReferences AndAlso SupportsDesktopReferences Then
                Merge(options.References, DesktopReferences)
            End If
        End Sub

        ''' <summary>
        ''' Appends <paramref name="defaults"/> to <paramref name="target"/> without replacing host entries and
        ''' deduplicates case-insensitively. Blank/<see langword="Nothing"/> entries are kept (only deduplicated)
        ''' so the options validator can reject them; this method never repairs host input.
        ''' </summary>
        Private Shared Sub Merge(target As IList(Of String), defaults As ImmutableArray(Of String))
            Dim seen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            Dim kept As New List(Of String)(target.Count)
            For Each item In target
                ' Nothing and "" are the same blank entry for validation purposes.
                If seen.Add(If(item, String.Empty)) Then
                    kept.Add(item)
                End If
            Next

            If kept.Count <> target.Count Then
                target.Clear()
                For Each item In kept
                    target.Add(item)
                Next
            End If

            For Each item In defaults
                If seen.Add(item) Then
                    target.Add(item)
                End If
            Next
        End Sub

        Private Shared Function ParseEmbeddedResponseFile() As ParsedDefaults
            Dim assembly = GetType(VBScriptDefaults).GetTypeInfo().Assembly
            Dim stream = assembly.GetManifestResourceStream(ResourceName)
            If stream Is Nothing Then
                Throw New VBScriptHostException(
                    "The embedded default response file '" & ResourceName & "' is missing from " &
                    assembly.GetName().Name & ". Reinstall the package; the defaults cannot be reconstructed.")
            End If

            Using stream
                Using reader As New StreamReader(stream)
                    Return Parse(reader.ReadToEnd())
                End Using
            End Using
        End Function

        ''' <summary>Parses rsp text: <c>^/r:(.+)$</c> to references, <c>^/imports:(.+)$</c> to imports.</summary>
        Friend Shared Function Parse(text As String) As ParsedDefaults
            Dim references As New List(Of String)()
            Dim importNames As New List(Of String)()

            For Each rawLine In (If(text, String.Empty)).Split({ControlChars.CrLf, ControlChars.Lf, ControlChars.Cr}, StringSplitOptions.None)
                Dim line = rawLine.Trim()
                If line.Length = 0 OrElse line.StartsWith("#", StringComparison.Ordinal) Then
                    Continue For
                End If

                Dim match = s_referenceLine.Match(line)
                If match.Success Then
                    references.Add(match.Groups(1).Value.Trim())
                    Continue For
                End If

                match = s_importLine.Match(line)
                If match.Success Then
                    importNames.Add(match.Groups(1).Value.Trim())
                End If
            Next

            Return New ParsedDefaults(references.ToImmutableArray(), importNames.ToImmutableArray())
        End Function

        Friend NotInheritable Class ParsedDefaults
            Friend ReadOnly References As ImmutableArray(Of String)
            Friend ReadOnly [Imports] As ImmutableArray(Of String)

            Friend Sub New(references As ImmutableArray(Of String), importNames As ImmutableArray(Of String))
                Me.References = references
                Me.[Imports] = importNames
            End Sub
        End Class

    End Class

End Namespace
