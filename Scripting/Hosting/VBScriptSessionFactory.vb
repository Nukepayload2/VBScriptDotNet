' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports Microsoft.Extensions.Hosting

Namespace Nukepayload2.VBScriptDotNet.Hosting

    ''' <summary>
    ''' The singleton <see cref="IVBScriptSessionFactory"/>. It carries the host-owned pieces a session needs
    ''' (lifetime, output, the NuGet restore seam) and nothing else; the session itself is never registered in the
    ''' container.
    ''' </summary>
    Friend NotInheritable Class VBScriptSessionFactory
        Implements IVBScriptSessionFactory

        Private ReadOnly _lifetime As IHostApplicationLifetime
        Private ReadOnly _output As IScriptOutput
        Private ReadOnly _restoreService As INuGetRestoreService
        Private ReadOnly _packageResolver As INuGetPackageResolver

        Public Sub New(lifetime As IHostApplicationLifetime, output As IScriptOutput, restoreService As INuGetRestoreService, packageResolver As INuGetPackageResolver)
            If lifetime Is Nothing Then
                Throw New ArgumentNullException(NameOf(lifetime))
            End If

            If output Is Nothing Then
                Throw New ArgumentNullException(NameOf(output))
            End If

            If restoreService Is Nothing Then
                Throw New ArgumentNullException(NameOf(restoreService))
            End If

            If packageResolver Is Nothing Then
                Throw New ArgumentNullException(NameOf(packageResolver))
            End If

            _lifetime = lifetime
            _output = output
            _restoreService = restoreService
            _packageResolver = packageResolver
        End Sub

        Public Function CreateSession(options As VBScriptOptions) As IVBScriptSession Implements IVBScriptSessionFactory.CreateSession
            If options Is Nothing Then
                Throw New ArgumentNullException(
                    NameOf(options),
                    "CreateSession needs the VBScriptOptions the session starts from. Pass a VBScriptOptions instance " &
                    "(its default set is applied to a copy); the options can still be changed after the session is " &
                    "created because the session keeps its own snapshot.")
            End If

            Return New VBScriptSession(_lifetime, _output, _restoreService, _packageResolver, options)
        End Function

    End Class

End Namespace
