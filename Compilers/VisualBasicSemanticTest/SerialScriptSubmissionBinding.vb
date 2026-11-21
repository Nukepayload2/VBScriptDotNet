' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Namespace Microsoft.CodeAnalysis.VisualBasic.UnitTests

    ''' <summary>
    ''' issue 35: <c>ReferenceManager</c> checks its process-wide symbol cache under a lock, creates a
    ''' <c>PEAssemblySymbol</c> outside that lock, and publishes it under the lock again, so when another test class
    ''' compiles concurrently the same metadata can end up with two symbols, and a <c>previous:=</c> submission chain
    ''' then trips the <c>Binder_Conversions.vb:442</c> identity assert. A collection marked
    ''' <c>DisableParallelization</c> never runs alongside any other collection, so that "another class compiles at the
    ''' same time" condition cannot arise - independent of which class the neighbour would have been.
    ''' </summary>
    <CollectionDefinition(NameOf(SerialScriptSubmissionBinding), DisableParallelization:=True)>
    Public NotInheritable Class SerialScriptSubmissionBinding
    End Class

End Namespace
