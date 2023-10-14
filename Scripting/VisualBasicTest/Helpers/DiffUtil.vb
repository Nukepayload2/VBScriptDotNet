Public Class DiffUtil
    Private Shared ReadOnly s_lineSplitChars() As Char = {ControlChars.Cr, ControlChars.Lf}

    Public Shared Function Lines(s As String) As String()
        Return s.Split(s_lineSplitChars, StringSplitOptions.RemoveEmptyEntries)
    End Function

	Public Shared Function DiffReport(Of T)(expected As IEnumerable(Of T), actual As IEnumerable(Of T), separator As String, Optional comparer As IEqualityComparer(Of T) = Nothing, Optional toString As Func(Of T, String) = Nothing) As String
		Dim lcs = If(comparer IsNot Nothing, New LCS(Of T)(comparer), DiffUtil.LCS(Of T).Default)
		toString = If(toString, New Func(Of T, String)(Function(obj) obj.ToString()))

		Dim expectedList As IList(Of T) = If(TryCast(expected, IList(Of T)), New List(Of T)(expected))
		Dim actualList As IList(Of T) = If(TryCast(actual, IList(Of T)), New List(Of T)(actual))

		Return String.Join(separator, lcs.CalculateDiff(expectedList, actualList, toString))
	End Function

	Private Class LCS(Of T)
		Inherits LongestCommonSubsequence(Of IList(Of T))

		Public Shared ReadOnly [Default] As New LCS(Of T)(EqualityComparer(Of T).Default)

		Private ReadOnly _comparer As IEqualityComparer(Of T)

		Public Sub New(comparer As IEqualityComparer(Of T))
			_comparer = comparer
		End Sub

		Protected Overrides Function ItemsEqual(sequenceA As IList(Of T), indexA As Integer, sequenceB As IList(Of T), indexB As Integer) As Boolean
			Return _comparer.Equals(sequenceA(indexA), sequenceB(indexB))
		End Function

		Public Iterator Function CalculateDiff(sequenceA As IList(Of T), sequenceB As IList(Of T), toString As Func(Of T, String)) As IEnumerable(Of String)
			For Each edit In GetEdits(sequenceA, sequenceA.Count, sequenceB, sequenceB.Count).Reverse()
				Select Case edit.Kind
					Case EditKind.Delete
						Yield "--> " & toString(sequenceA(edit.IndexA))

					Case EditKind.Insert
						Yield "++> " & toString(sequenceB(edit.IndexB))

					Case EditKind.Update
						Yield "    " & toString(sequenceB(edit.IndexB))
				End Select
			Next edit
		End Function
	End Class

	Private Enum EditKind
		''' <summary>
		''' No change.
		''' </summary>
		None = 0

		''' <summary>
		''' Node value was updated.
		''' </summary>
		Update = 1

		''' <summary>
		''' Node was inserted.
		''' </summary>
		Insert = 2

		''' <summary>
		''' Node was deleted.
		''' </summary>
		Delete = 3
	End Enum

	''' <summary>
	''' Calculates Longest Common Subsequence.
	''' </summary>
	Private MustInherit Class LongestCommonSubsequence(Of TSequence)
		Protected Structure Edit
			Public ReadOnly Kind As EditKind
			Public ReadOnly IndexA As Integer
			Public ReadOnly IndexB As Integer

			Friend Sub New(kind_Renamed As EditKind, indexA_Renamed As Integer, indexB_Renamed As Integer)
				Me.Kind = kind_Renamed
				Me.IndexA = indexA_Renamed
				Me.IndexB = indexB_Renamed
			End Sub
		End Structure

		Private Const DeleteCost As Integer = 1
		Private Const InsertCost As Integer = 1
		Private Const UpdateCost As Integer = 2

		Protected MustOverride Function ItemsEqual(sequenceA As TSequence, indexA As Integer, sequenceB As TSequence, indexB As Integer) As Boolean

		Protected Iterator Function GetMatchingPairs(sequenceA As TSequence, lengthA As Integer, sequenceB As TSequence, lengthB As Integer) As IEnumerable(Of KeyValuePair(Of Integer, Integer))
			Dim d(,) As Integer = ComputeCostMatrix(sequenceA, lengthA, sequenceB, lengthB)
			Dim i As Integer = lengthA
			Dim j As Integer = lengthB

			Do While i <> 0 AndAlso j <> 0
				If d(i, j) = d(i - 1, j) + DeleteCost Then
					i -= 1
				ElseIf d(i, j) = d(i, j - 1) + InsertCost Then
					j -= 1
				Else
					i -= 1
					j -= 1
					Yield New KeyValuePair(Of Integer, Integer)(i, j)
				End If
			Loop
		End Function

		Protected Iterator Function GetEdits(sequenceA As TSequence, lengthA As Integer, sequenceB As TSequence, lengthB As Integer) As IEnumerable(Of Edit)
			Dim d(,) As Integer = ComputeCostMatrix(sequenceA, lengthA, sequenceB, lengthB)
			Dim i As Integer = lengthA
			Dim j As Integer = lengthB

			Do While i <> 0 AndAlso j <> 0
				If d(i, j) = d(i - 1, j) + DeleteCost Then
					i -= 1
					Yield New Edit(EditKind.Delete, i, -1)
				ElseIf d(i, j) = d(i, j - 1) + InsertCost Then
					j -= 1
					Yield New Edit(EditKind.Insert, -1, j)
				Else
					i -= 1
					j -= 1
					Yield New Edit(EditKind.Update, i, j)
				End If
			Loop

			Do While i > 0
				i -= 1
				Yield New Edit(EditKind.Delete, i, -1)
			Loop

			Do While j > 0
				j -= 1
				Yield New Edit(EditKind.Insert, -1, j)
			Loop
		End Function

		''' <summary>
		''' Returns a distance [0..1] of the specified sequences.
		''' The smaller distance the more of their elements match.
		''' </summary>
		''' <summary>
		''' Returns a distance [0..1] of the specified sequences.
		''' The smaller distance the more of their elements match.
		''' </summary>
		Protected Function ComputeDistance(sequenceA As TSequence, lengthA As Integer, sequenceB As TSequence, lengthB As Integer) As Double
			Debug.Assert(lengthA >= 0 AndAlso lengthB >= 0)

			If lengthA = 0 OrElse lengthB = 0 Then
				Return If(lengthA = lengthB, 0.0, 1.0)
			End If

			Dim lcsLength As Integer = 0
			For Each pair In GetMatchingPairs(sequenceA, lengthA, sequenceB, lengthB)
				lcsLength += 1
			Next pair

			Dim max As Integer = Math.Max(lengthA, lengthB)
			Debug.Assert(lcsLength <= max)
			Return 1.0 - CDbl(lcsLength) / CDbl(max)
		End Function

		''' <summary>
		''' Calculates costs of all paths in an edit graph starting from vertex (0,0) and ending in vertex (lengthA, lengthB). 
		''' </summary>
		''' <remarks>
		''' The edit graph for A and B has a vertex at each point in the grid (i,j), i in [0, lengthA] and j in [0, lengthB].
		''' 
		''' The vertices of the edit graph are connected by horizontal, vertical, and diagonal directed edges to form a directed acyclic graph.
		''' Horizontal edges connect each vertex to its right neighbor. 
		''' Vertical edges connect each vertex to the neighbor below it.
		''' Diagonal edges connect vertex (i,j) to vertex (i-1,j-1) if <see cref="ItemsEqual"/>(sequenceA[i-1],sequenceB[j-1]) is true.
		''' 
		''' Editing starts with S = []. 
		''' Move along horizontal edge (i-1,j)-(i,j) represents the fact that sequenceA[i-1] is not added to S.
		''' Move along vertical edge (i,j-1)-(i,j) represents an insert of sequenceB[j-1] to S.
		''' Move along diagonal edge (i-1,j-1)-(i,j) represents an addition of sequenceB[j-1] to S via an acceptable 
		''' change of sequenceA[i-1] to sequenceB[j-1].
		''' 
		''' In every vertex the cheapest outgoing edge is selected. 
		''' The number of diagonal edges on the path from (0,0) to (lengthA, lengthB) is the length of the longest common subsequence.
		''' </remarks>
		Private Function ComputeCostMatrix(sequenceA As TSequence, lengthA As Integer, sequenceB As TSequence, lengthB As Integer) As Integer(,)
			Dim la = lengthA + 1
			Dim lb = lengthB + 1

			' TODO: Optimization possible: O(ND) time, O(N) space
			' EUGENE W. MYERS: An O(ND) Difference Algorithm and Its Variations
			Dim d = New Integer(la - 1, lb - 1) {}

			d(0, 0) = 0
			For i As Integer = 1 To lengthA
				d(i, 0) = d(i - 1, 0) + DeleteCost
			Next i

			For j As Integer = 1 To lengthB
				d(0, j) = d(0, j - 1) + InsertCost
			Next j

			For i As Integer = 1 To lengthA
				For j As Integer = 1 To lengthB
					Dim m1 As Integer = d(i - 1, j - 1) + (If(ItemsEqual(sequenceA, i - 1, sequenceB, j - 1), 0, UpdateCost))
					Dim m2 As Integer = d(i - 1, j) + DeleteCost
					Dim m3 As Integer = d(i, j - 1) + InsertCost
					d(i, j) = Math.Min(Math.Min(m1, m2), m3)
				Next j
			Next i

			Return d
		End Function
	End Class

End Class
