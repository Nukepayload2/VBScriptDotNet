' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Collections.Generic
Imports System.Collections.Immutable
Imports Microsoft.CodeAnalysis.PooledObjects
Imports Microsoft.CodeAnalysis.VisualBasic.Symbols
Imports Microsoft.CodeAnalysis.VisualBasic.Symbols.Metadata.PE
Imports Microsoft.CodeAnalysis.VisualBasic.Symbols.Retargeting
Imports Microsoft.CodeAnalysis.VisualBasic.Syntax

Namespace Microsoft.CodeAnalysis.VisualBasic.Symbols

    ''' <summary>
    ''' Entry point for reducing C# 14 extension members (methods / properties / operators that were
    ''' collected from &lt;G&gt;$ extension grouping types) into receiver-exposing symbols that the
    ''' binder and operator-resolution layers (F15) consume.
    '''
    ''' Shape notes:
    ''' - Extension members live as members of a &lt;G&gt;$ grouping type. The receiver is NOT an
    '''   explicit parameter on methods/properties; it lives on the grouping type's nested
    '''   &lt;M&gt;$ marker type's &lt;Extension&gt;$(receiver) method. Operators keep the receiver as
    '''   their first parameter.
    ''' - Generic extension blocks (extension(Of T)(value)) make the grouping type generic; the
    '''   receiver type is then the grouping type's own type parameter, which must be inferred from
    '''   the instance type and fixed before constructing the reduced symbol.
    ''' </summary>
    Friend Module ReducedExtensionMemberReducer

        ''' <summary>
        ''' Reduces a collected C# 14 extension member against the given instance type.
        ''' Returns Nothing when the member is not reducible against the instance type.
        ''' </summary>
        Public Function ReduceExtensionMember(
            instanceType As TypeSymbol,
            extensionMember As Symbol,
            ByRef useSiteInfo As CompoundUseSiteInfo(Of AssemblySymbol),
            languageVersion As LanguageVersion,
            proximity As Integer
        ) As Symbol
            If extensionMember Is Nothing OrElse instanceType Is Nothing Then
                Return Nothing
            End If

            Select Case extensionMember.Kind
                Case SymbolKind.Property
                    Dim prop = TryCast(extensionMember, PropertySymbol)
                    If prop IsNot Nothing AndAlso prop.IsExtensionMember Then
                        Return ReduceExtensionProperty(instanceType, prop, useSiteInfo, languageVersion)
                    End If

                Case SymbolKind.Method
                    Dim method = TryCast(extensionMember, MethodSymbol)
                    If method IsNot Nothing AndAlso method.IsExtensionMember Then
                        ' Extension operators land as MethodKind.Ordinary in the PE symbol model
                        ' (F13 evidence: ValidateOverloadedOperator fails for the grouping-type
                        ' host), so detect them by their op_ name rather than MethodKind.
                        If OverloadResolution.GetOperatorInfo(method.Name).ParamCount <> 0 Then
                            Return ReduceExtensionOperator(instanceType, method, useSiteInfo, languageVersion)
                        End If

                        ' C# 14 extension method: reduce the corresponding top-level [Extension]
                        ' shim (receiver-as-parameter form), reusing the proven classic path.
                        Return ReduceExtensionMethodViaShim(instanceType, method, useSiteInfo, languageVersion, proximity)
                    End If
            End Select

            Return Nothing
        End Function

        ''' <summary>
        ''' Locates the corresponding top-level static "shim" method of a grouping-type member,
        ''' i.e. the [Extension]-marked method on the [Extension] container that takes the receiver
        ''' as an explicit first parameter (this is the actual callable emitted by the C# compiler).
        ''' Matches by name and by arity == groupingType.Arity + member.Arity.
        ''' </summary>
        Friend Function FindTopLevelShim(groupingMember As MethodSymbol) As MethodSymbol
            Dim groupingType = groupingMember.ContainingType
            If groupingType Is Nothing Then
                Return Nothing
            End If

            Dim topLevel = groupingType.ContainingType
            If topLevel Is Nothing Then
                Return Nothing
            End If

            Dim expectedArity = groupingType.Arity + groupingMember.Arity

            For Each candidate As Symbol In topLevel.GetMembers(groupingMember.Name)
                Dim candidateMethod = TryCast(candidate, MethodSymbol)
                If candidateMethod IsNot Nothing AndAlso candidateMethod.IsShared AndAlso
                   candidateMethod.Arity = expectedArity AndAlso Not candidateMethod.IsExtensionMember Then
                    Return candidateMethod
                End If
            Next

            Return Nothing
        End Function

        ''' <summary>
        ''' Returns the extension receiver type for the grouping type that contains the given
        ''' extension member, or Nothing.
        ''' </summary>
        Friend Function GetReceiverType(member As Symbol) As TypeSymbol
            Dim groupingType = TryCast(member.ContainingType, NamedTypeSymbol)
            If groupingType Is Nothing Then
                Return Nothing
            End If

            Dim peGrouping = TryCast(UnwrapGroupingType(groupingType), PENamedTypeSymbol)
            If peGrouping Is Nothing Then
                Return Nothing
            End If

            Return peGrouping.GetExtensionReceiverType()
        End Function

        Private Function UnwrapGroupingType(groupingType As NamedTypeSymbol) As NamedTypeSymbol
            ' RetargetingNamedTypeSymbol wraps a PE grouping type; unwrap to read the receiver.
            Dim retargeting = TryCast(groupingType, RetargetingNamedTypeSymbol)
            If retargeting IsNot Nothing Then
                Return retargeting.UnderlyingNamedType
            End If
            Return groupingType
        End Function

        ' ----------------------------------------------------------------------------------------
        ' Property reduction (3a / 3b)
        ' ----------------------------------------------------------------------------------------

        Private Function ReduceExtensionProperty(
            instanceType As TypeSymbol,
            extensionProperty As PropertySymbol,
            ByRef useSiteInfo As CompoundUseSiteInfo(Of AssemblySymbol),
            languageVersion As LanguageVersion
        ) As PropertySymbol
            Dim receiverType As TypeSymbol = GetReceiverType(extensionProperty)
            If receiverType Is Nothing Then
                Return Nothing
            End If

            Dim groupingType = extensionProperty.ContainingType

            ' Collect type parameters referenced by the receiver type (typically the grouping
            ' type's own type parameters for a generic extension block).
            Dim typeParametersToFix As ImmutableArray(Of TypeParameterSymbol) = Nothing
            Dim fixWith As ImmutableArray(Of TypeSymbol) = Nothing
            Dim reducedReceiverType As TypeSymbol = receiverType
            Dim constructedGrouping As NamedTypeSymbol = groupingType

            If InferGroupingTypeArguments(groupingType, receiverType, instanceType,
                                          useSiteInfo, languageVersion,
                                          typeParametersToFix, fixWith, reducedReceiverType) Then

                If typeParametersToFix.Length > 0 Then
                    ' Construct the grouping type with the fixed (and unfixed) type arguments and
                    ' look up the property on the constructed type so its signature is substituted.
                    constructedGrouping = ConstructGroupingType(groupingType, typeParametersToFix, fixWith)
                    If constructedGrouping Is Nothing Then
                        Return Nothing
                    End If

                    Dim substitutedProperty = FindPropertyByName(constructedGrouping, extensionProperty.Name)
                    If substitutedProperty Is Nothing Then
                        Return Nothing
                    End If

                    extensionProperty = substitutedProperty
                End If

                Return New ReducedExtensionPropertySymbol(extensionProperty, reducedReceiverType)
            End If

            Return Nothing
        End Function

        ' ----------------------------------------------------------------------------------------
        ' Operator reduction (3c)
        ' ----------------------------------------------------------------------------------------

        Private Function ReduceExtensionOperator(
            instanceType As TypeSymbol,
            operatorMethod As MethodSymbol,
            ByRef useSiteInfo As CompoundUseSiteInfo(Of AssemblySymbol),
            languageVersion As LanguageVersion
        ) As MethodSymbol
            If operatorMethod.ParameterCount = 0 Then
                Return Nothing
            End If

            ' The extension receiver is the first (left-operand) parameter of an operator.
            Dim receiverType As TypeSymbol = operatorMethod.Parameters(0).Type
            Dim groupingType = operatorMethod.ContainingType

            Dim typeParametersToFix As ImmutableArray(Of TypeParameterSymbol) = Nothing
            Dim fixWith As ImmutableArray(Of TypeSymbol) = Nothing
            Dim reducedReceiverType As TypeSymbol = receiverType
            Dim constructedGrouping As NamedTypeSymbol = groupingType

            If InferGroupingTypeArguments(groupingType, receiverType, instanceType,
                                          useSiteInfo, languageVersion,
                                          typeParametersToFix, fixWith, reducedReceiverType) Then

                If typeParametersToFix.Length > 0 Then
                    constructedGrouping = ConstructGroupingType(groupingType, typeParametersToFix, fixWith)
                    If constructedGrouping Is Nothing Then
                        Return Nothing
                    End If

                    operatorMethod = FindMethodByName(constructedGrouping, operatorMethod.Name)
                    If operatorMethod Is Nothing OrElse operatorMethod.ParameterCount = 0 Then
                        Return Nothing
                    End If

                    reducedReceiverType = operatorMethod.Parameters(0).Type
                End If

                ' The grouping type's operator is a throwing stub (newobj; throw); the real
                ' implementation is the top-level (non-extension) method with the same signature
                ' on the [Extension] container, so prefer it as the call target. For generic
                ' extension blocks the top-level operator is generic and is not substituted here
                ' (concrete-type operands are the current scope).
                If groupingType.Arity = 0 Then
                    Dim shim As MethodSymbol = FindTopLevelShim(operatorMethod)
                    If shim IsNot Nothing Then
                        operatorMethod = shim
                    End If
                End If

                Return New ReducedExtensionOperatorSymbol(operatorMethod, reducedReceiverType)
            End If

            Return Nothing
        End Function

        ' ----------------------------------------------------------------------------------------
        ' Method reduction via the top-level [Extension] shim (reuses the classic path)
        ' ----------------------------------------------------------------------------------------

        Private Function ReduceExtensionMethodViaShim(
            instanceType As TypeSymbol,
            groupingMethod As MethodSymbol,
            ByRef useSiteInfo As CompoundUseSiteInfo(Of AssemblySymbol),
            languageVersion As LanguageVersion,
            proximity As Integer
        ) As MethodSymbol
            Dim shim As MethodSymbol = FindTopLevelShim(groupingMethod)
            If shim Is Nothing Then
                Return Nothing
            End If

            Return ReducedExtensionMethodSymbol.Create(instanceType, shim, proximity, useSiteInfo, languageVersion)
        End Function

        ' ----------------------------------------------------------------------------------------
        ' Shared inference core (mirrors ReducedExtensionMethodSymbol.Create :66-180)
        ' ----------------------------------------------------------------------------------------

        ''' <summary>
        ''' Infers the type parameters referenced by the receiver type from the instance type using a
        ''' synthetic method (whose type parameters are the grouping type's) as the inference target,
        ''' and verifies constraints. On success returns the fixed type-parameter pairs and the
        ''' substituted receiver type.
        ''' </summary>
        Private Function InferGroupingTypeArguments(
            groupingType As NamedTypeSymbol,
            receiverType As TypeSymbol,
            instanceType As TypeSymbol,
            ByRef useSiteInfo As CompoundUseSiteInfo(Of AssemblySymbol),
            languageVersion As LanguageVersion,
            ByRef typeParametersToFix As ImmutableArray(Of TypeParameterSymbol),
            ByRef fixWith As ImmutableArray(Of TypeSymbol),
            ByRef reducedReceiverType As TypeSymbol
        ) As Boolean
            typeParametersToFix = ImmutableArray(Of TypeParameterSymbol).Empty
            fixWith = ImmutableArray(Of TypeSymbol).Empty
            reducedReceiverType = receiverType

            If groupingType.Arity = 0 Then
                Return True
            End If

            Dim hashSetOfTypeParametersToFix As New HashSet(Of TypeParameterSymbol)
            receiverType.CollectReferencedTypeParameters(hashSetOfTypeParametersToFix)

            If hashSetOfTypeParametersToFix.Count = 0 Then
                Return True
            End If

            ' Build a synthetic method representing the extension receiver: type parameters = the
            ' grouping type's type parameters, single parameter = the receiver type. This lets the
            ' existing TypeArgumentInference machinery infer the receiver-referenced type parameters
            ' from the instance type.
            Dim receiverParam As New SignatureOnlyParameterSymbol(receiverType,
                                                                  ImmutableArray(Of CustomModifier).Empty,
                                                                  ImmutableArray(Of CustomModifier).Empty,
                                                                  Nothing,
                                                                  isParamArray:=False,
                                                                  isByRef:=False,
                                                                  isOut:=False,
                                                                  isOptional:=False)
            Dim synthetic As New SignatureOnlyMethodSymbol(
                name:=WellKnownMemberNames.ExtensionMarkerMethodName,
                m_containingType:=groupingType,
                methodKind:=MethodKind.Ordinary,
                callingConvention:=Microsoft.Cci.CallingConvention.Default,
                typeParameters:=groupingType.TypeParameters,
                parameters:=ImmutableArray.Create(Of ParameterSymbol)(receiverParam),
                returnsByRef:=False,
                returnType:=receiverType,
                returnTypeCustomModifiers:=ImmutableArray(Of CustomModifier).Empty,
                refCustomModifiers:=ImmutableArray(Of CustomModifier).Empty,
                explicitInterfaceImplementations:=ImmutableArray(Of MethodSymbol).Empty)

            Dim reducedUseSiteInfo = If(useSiteInfo.AccumulatesDependencies,
                                        New CompoundUseSiteInfo(Of AssemblySymbol)(useSiteInfo.AssemblyBeingBuilt),
                                        CompoundUseSiteInfo(Of AssemblySymbol).DiscardedDependencies)

            Dim parameterToArgumentMap = ArrayBuilder(Of Integer).GetInstance(1, -1)
            parameterToArgumentMap(0) = 0

            Dim typeArguments As ImmutableArray(Of TypeSymbol) = Nothing
            Dim inferenceLevel As TypeArgumentInference.InferenceLevel = TypeArgumentInference.InferenceLevel.None
            Dim allFailedInferenceIsDueToObject As Boolean = False
            Dim someInferenceFailed As Boolean = False
            Dim inferenceErrorReasons As InferenceErrorReasons = InferenceErrorReasons.Other

            Dim fixTheseTypeParameters = BitVector.Create(synthetic.Arity)

            For Each typeParameter As TypeParameterSymbol In hashSetOfTypeParametersToFix
                fixTheseTypeParameters(typeParameter.Ordinal) = True
            Next

            Dim inferenceDiagnostic = If(reducedUseSiteInfo.AccumulatesDependencies,
                                         BindingDiagnosticBag.GetInstance(withDiagnostics:=False, withDependencies:=True),
                                         BindingDiagnosticBag.Discarded)

            Dim success As Boolean = TypeArgumentInference.Infer(synthetic,
                                           arguments:=ImmutableArray.Create(Of BoundExpression)(
                                               New BoundRValuePlaceholder(VisualBasic.VisualBasicSyntaxTree.Dummy.GetRoot(Nothing),
                                                                         instanceType)),
                                           parameterToArgumentMap:=parameterToArgumentMap,
                                           paramArrayItems:=Nothing,
                                           delegateReturnType:=Nothing,
                                           delegateReturnTypeReferenceBoundNode:=Nothing,
                                           typeArguments:=typeArguments,
                                           inferenceLevel:=inferenceLevel,
                                           someInferenceFailed:=someInferenceFailed,
                                           allFailedInferenceIsDueToObject:=allFailedInferenceIsDueToObject,
                                           inferenceErrorReasons:=inferenceErrorReasons,
                                           inferredTypeByAssumption:=Nothing,
                                           typeArgumentsLocation:=Nothing,
                                           asyncLambdaSubToFunctionMismatch:=Nothing,
                                           useSiteInfo:=reducedUseSiteInfo,
                                           diagnostic:=inferenceDiagnostic,
                                           inferTheseTypeParameters:=fixTheseTypeParameters)

            parameterToArgumentMap.Free()

            If Not success OrElse Not reducedUseSiteInfo.Diagnostics.IsNullOrEmpty() Then
                inferenceDiagnostic.Free()
                Return False
            End If

            reducedUseSiteInfo.AddDependencies(inferenceDiagnostic.DependenciesBag)
            inferenceDiagnostic.Free()

            Dim toFixCount = hashSetOfTypeParametersToFix.Count
            Dim typeParametersToFixBuilder = ArrayBuilder(Of TypeParameterSymbol).GetInstance(toFixCount)
            Dim fixWithBuilder = ArrayBuilder(Of TypeSymbol).GetInstance(toFixCount)

            For i As Integer = 0 To synthetic.Arity - 1
                If fixTheseTypeParameters(i) Then
                    typeParametersToFixBuilder.Add(synthetic.TypeParameters(i))
                    fixWithBuilder.Add(typeArguments(i))

                    If typeParametersToFixBuilder.Count = toFixCount Then
                        Exit For
                    End If
                End If
            Next

            typeParametersToFix = typeParametersToFixBuilder.ToImmutableAndFree()
            fixWith = fixWithBuilder.ToImmutableAndFree()

            Dim partialSubstitution = TypeSubstitution.Create(groupingType, typeParametersToFix, fixWith)

            If partialSubstitution IsNot Nothing Then
                ' Check constraints.
                Dim diagnosticsBuilder = ArrayBuilder(Of TypeParameterDiagnosticInfo).GetInstance()
                Dim useSiteDiagnosticsBuilder As ArrayBuilder(Of TypeParameterDiagnosticInfo) = Nothing
                Dim constructedGrouping = ConstructGroupingType(groupingType, typeParametersToFix, fixWith)
                If constructedGrouping Is Nothing Then
                    diagnosticsBuilder.Free()
                    Return False
                End If

                success = constructedGrouping.CheckConstraints(languageVersion,
                                                               partialSubstitution,
                                                               typeParametersToFix,
                                                               fixWith,
                                                               diagnosticsBuilder,
                                                               useSiteDiagnosticsBuilder,
                                                               template:=New CompoundUseSiteInfo(Of AssemblySymbol)(reducedUseSiteInfo))

                If Not success Then
                    diagnosticsBuilder.Free()
                    Return False
                End If

                If useSiteDiagnosticsBuilder IsNot Nothing Then
                    diagnosticsBuilder.AddRange(useSiteDiagnosticsBuilder)
                End If

                For Each pair In diagnosticsBuilder
                    reducedUseSiteInfo.AddDependencies(pair.UseSiteInfo)
                Next

                diagnosticsBuilder.Free()

                reducedReceiverType = receiverType.InternalSubstituteTypeParameters(partialSubstitution).Type
            End If

            If Not OverloadResolution.DoesReceiverMatchInstance(instanceType, reducedReceiverType, reducedUseSiteInfo) OrElse
               Not reducedUseSiteInfo.Diagnostics.IsNullOrEmpty() Then
                Return False
            End If

            useSiteInfo.AddDependencies(reducedUseSiteInfo)

            Return True
        End Function

        ''' <summary>
        ''' Constructs the grouping type with the given fixed type parameters; unfixed type
        ''' parameters are filled with the type parameters themselves (mirrors the C# behavior).
        ''' </summary>
        Private Function ConstructGroupingType(
            groupingType As NamedTypeSymbol,
            typeParametersToFix As ImmutableArray(Of TypeParameterSymbol),
            fixWith As ImmutableArray(Of TypeSymbol)
        ) As NamedTypeSymbol
            If groupingType.Arity = 0 Then
                Return groupingType
            End If

            Dim fullTypeArgs(groupingType.Arity - 1) As TypeSymbol

            For i As Integer = 0 To groupingType.Arity - 1
                Dim tp = groupingType.TypeParameters(i)
                fullTypeArgs(i) = tp

                For j As Integer = 0 To typeParametersToFix.Length - 1
                    If TypeSymbol.Equals(typeParametersToFix(j), tp, TypeCompareKind.ConsiderEverything) Then
                        fullTypeArgs(i) = fixWith(j)
                        Exit For
                    End If
                Next
            Next

            Return groupingType.Construct(fullTypeArgs)
        End Function

        Private Function FindPropertyByName(type As NamedTypeSymbol, name As String) As PropertySymbol
            For Each member As Symbol In type.GetMembers(name)
                Dim prop = TryCast(member, PropertySymbol)
                If prop IsNot Nothing Then
                    Return prop
                End If
            Next
            Return Nothing
        End Function

        Private Function FindMethodByName(type As NamedTypeSymbol, name As String) As MethodSymbol
            For Each member As Symbol In type.GetMembers(name)
                Dim method = TryCast(member, MethodSymbol)
                If method IsNot Nothing Then
                    Return method
                End If
            Next
            Return Nothing
        End Function
    End Module

    ''' <summary>
    ''' A C# 14 extension operator in reduced form. The operator keeps all of its operands as
    ''' parameters (the extension receiver is the first, left-operand parameter), exposes the
    ''' receiver as <see cref="ReceiverType"/>, and is presented with
    ''' <see cref="MethodKind.UserDefinedOperator"/> so that it flows through the existing
    ''' operator-resolution machinery. Because <see cref="IsMethodKindBasedOnSyntax"/> is False,
    ''' the <c>ValidateOverloadedOperator</c> check (which would reject an operator whose containing
    ''' type is not an operand type) is correctly skipped.
    ''' </summary>
    Friend NotInheritable Class ReducedExtensionOperatorSymbol
        Inherits WrappedMethodSymbol

        Private ReadOnly _originalDefinition As MethodSymbol
        Private ReadOnly _receiverType As TypeSymbol

        Public Sub New(originalDefinition As MethodSymbol, receiverType As TypeSymbol)
            Debug.Assert(originalDefinition IsNot Nothing)
            Debug.Assert(receiverType IsNot Nothing)
            Debug.Assert(originalDefinition.ParameterCount > 0)
            _originalDefinition = originalDefinition
            _receiverType = receiverType
        End Sub

        Public Overrides ReadOnly Property UnderlyingMethod As MethodSymbol
            Get
                Return _originalDefinition
            End Get
        End Property

        Public Overrides ReadOnly Property ReceiverType As TypeSymbol
            Get
                Return _receiverType
            End Get
        End Property

        ' The reduced operator keeps all of its operands (the receiver is the first parameter),
        ' so unlike a reduced extension method it is NOT receiver-stripped. Reporting a non-Nothing
        ' ReducedFrom would make MethodSymbol.IsReducedExtensionMethod true and cause overload
        ' resolution to treat it as a reduced extension method (e.g. MethodCandidate's assert and
        ' GetParameterTypeFromVirtualSignature's paramIndex+1), which is wrong for an operator.
        Public Overrides ReadOnly Property ReducedFrom As MethodSymbol
            Get
                Return Nothing
            End Get
        End Property

        Friend Overrides ReadOnly Property CallsiteReducedFromMethod As MethodSymbol
            Get
                Return _originalDefinition
            End Get
        End Property

        Friend Overrides ReadOnly Property FixedTypeParameters As ImmutableArray(Of KeyValuePair(Of TypeParameterSymbol, TypeSymbol))
            Get
                Return ImmutableArray(Of KeyValuePair(Of TypeParameterSymbol, TypeSymbol)).Empty
            End Get
        End Property

        Public Overrides ReadOnly Property MethodKind As MethodKind
            Get
                Return MethodKind.UserDefinedOperator
            End Get
        End Property

        Friend Overrides ReadOnly Property IsMethodKindBasedOnSyntax As Boolean
            Get
                Return False
            End Get
        End Property

        Friend Overrides ReadOnly Property MayBeReducibleExtensionMethod As Boolean
            Get
                Return False
            End Get
        End Property

        Public Overrides ReadOnly Property ContainingSymbol As Symbol
            Get
                Return _originalDefinition.ContainingSymbol
            End Get
        End Property

        Public Overrides ReadOnly Property ContainingType As NamedTypeSymbol
            Get
                Return _originalDefinition.ContainingType
            End Get
        End Property

        Public Overrides ReadOnly Property Parameters As ImmutableArray(Of ParameterSymbol)
            Get
                Return _originalDefinition.Parameters
            End Get
        End Property

        Public Overrides ReadOnly Property ReturnType As TypeSymbol
            Get
                Return _originalDefinition.ReturnType
            End Get
        End Property

        Public Overrides ReadOnly Property TypeParameters As ImmutableArray(Of TypeParameterSymbol)
            Get
                Return _originalDefinition.TypeParameters
            End Get
        End Property

        Public Overrides ReadOnly Property TypeArguments As ImmutableArray(Of TypeSymbol)
            Get
                Return _originalDefinition.TypeArguments
            End Get
        End Property

        Public Overrides ReadOnly Property ReturnTypeCustomModifiers As ImmutableArray(Of CustomModifier)
            Get
                Return _originalDefinition.ReturnTypeCustomModifiers
            End Get
        End Property

        Public Overrides ReadOnly Property RefCustomModifiers As ImmutableArray(Of CustomModifier)
            Get
                Return _originalDefinition.RefCustomModifiers
            End Get
        End Property

        Public Overrides ReadOnly Property IsSub As Boolean
            Get
                Return _originalDefinition.IsSub
            End Get
        End Property

        Public Overrides ReadOnly Property ExplicitInterfaceImplementations As ImmutableArray(Of MethodSymbol)
            Get
                Return _originalDefinition.ExplicitInterfaceImplementations
            End Get
        End Property

        Public Overrides ReadOnly Property AssociatedSymbol As Symbol
            Get
                Return Nothing
            End Get
        End Property

        Public Overrides Function GetOverloadResolutionPriority() As Integer
            Return _originalDefinition.GetOverloadResolutionPriority()
        End Function

        Public Overrides ReadOnly Property MetadataName As String
            Get
                Return _originalDefinition.MetadataName
            End Get
        End Property

        Friend Overrides Function GetUseSiteInfo() As UseSiteInfo(Of AssemblySymbol)
            Return _originalDefinition.GetUseSiteInfo()
        End Function

        Friend Overrides Function CalculateLocalSyntaxOffset(localPosition As Integer, localTree As SyntaxTree) As Integer
            Throw ExceptionUtilities.Unreachable
        End Function

        Friend Overrides ReadOnly Property HasSetsRequiredMembers As Boolean
            Get
                Return False
            End Get
        End Property

        Public Overrides Function GetHashCode() As Integer
            Return Hash.Combine(_receiverType.GetHashCode(), _originalDefinition.GetHashCode())
        End Function

        Public Overrides Function Equals(obj As Object) As Boolean
            If obj Is Me Then
                Return True
            End If

            Dim other = TryCast(obj, ReducedExtensionOperatorSymbol)

            Return other IsNot Nothing AndAlso
                   other._originalDefinition.Equals(_originalDefinition) AndAlso
                   other._receiverType.Equals(_receiverType)
        End Function
    End Class
End Namespace
