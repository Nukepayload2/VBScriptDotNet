﻿'------------------------------------------------------------------------------
' <auto-generated>
'     此代码由工具生成。
'     运行时版本:4.0.30319.42000
'
'     对此文件的更改可能会导致不正确的行为，并且如果
'     重新生成代码，这些更改将会丢失。
' </auto-generated>
'------------------------------------------------------------------------------

Option Strict On
Option Explicit On

Imports System

Namespace My.Resources
    
    '此类是由 StronglyTypedResourceBuilder
    '类通过类似于 ResGen 或 Visual Studio 的工具自动生成的。
    '若要添加或移除成员，请编辑 .ResX 文件，然后重新运行 ResGen
    '(以 /str 作为命令选项)，或重新生成 VS 项目。
    '''<summary>
    '''  一个强类型的资源类，用于查找本地化的字符串等。
    '''</summary>
    <Global.System.CodeDom.Compiler.GeneratedCodeAttribute("System.Resources.Tools.StronglyTypedResourceBuilder", "17.0.0.0"),  _
     Global.System.Diagnostics.DebuggerNonUserCodeAttribute(),  _
     Global.System.Runtime.CompilerServices.CompilerGeneratedAttribute()>  _
    Friend Class VBScriptingResources
        
        Private Shared resourceMan As Global.System.Resources.ResourceManager
        
        Private Shared resourceCulture As Global.System.Globalization.CultureInfo
        
        <Global.System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")>  _
        Friend Sub New()
            MyBase.New
        End Sub
        
        '''<summary>
        '''  返回此类使用的缓存的 ResourceManager 实例。
        '''</summary>
        <Global.System.ComponentModel.EditorBrowsableAttribute(Global.System.ComponentModel.EditorBrowsableState.Advanced)>  _
        Friend Shared ReadOnly Property ResourceManager() As Global.System.Resources.ResourceManager
            Get
                If Object.ReferenceEquals(resourceMan, Nothing) Then
                    Dim temp As Global.System.Resources.ResourceManager = New Global.System.Resources.ResourceManager("VBScriptingResources", GetType(VBScriptingResources).Assembly)
                    resourceMan = temp
                End If
                Return resourceMan
            End Get
        End Property
        
        '''<summary>
        '''  重写当前线程的 CurrentUICulture 属性，对
        '''  使用此强类型资源类的所有资源查找执行重写。
        '''</summary>
        <Global.System.ComponentModel.EditorBrowsableAttribute(Global.System.ComponentModel.EditorBrowsableState.Advanced)>  _
        Friend Shared Property Culture() As Global.System.Globalization.CultureInfo
            Get
                Return resourceCulture
            End Get
            Set
                resourceCulture = value
            End Set
        End Property
        
        '''<summary>
        '''  查找类似 Cannot escape non-printable characters in Visual Basic notation unless quotes are used. 的本地化字符串。
        '''</summary>
        Friend Shared ReadOnly Property ExceptionEscapeWithoutQuote() As String
            Get
                Return ResourceManager.GetString("ExceptionEscapeWithoutQuote", resourceCulture)
            End Get
        End Property
        
        '''<summary>
        '''  查找类似 Usage: vbi [options] [script-file.vbx] [-- script-arguments]
        '''
        '''If script-file is specified executes the script, otherwise launches an interactive REPL (Read Eval Print Loop).
        '''
        '''Options:
        '''  /help                          Display this usage message (Short form: /?)
        '''  /version                       Display the version and exit
        '''  /reference:&lt;alias&gt;=&lt;file&gt;      Reference metadata from the specified assembly file using the given alias (Short form: /r)
        '''  /reference:&lt;file list&gt;         Reference metadata from  [字符串的其余部分被截断]&quot;; 的本地化字符串。
        '''</summary>
        Friend Shared ReadOnly Property InteractiveHelp() As String
            Get
                Return ResourceManager.GetString("InteractiveHelp", resourceCulture)
            End Get
        End Property
        
        '''<summary>
        '''  查找类似 Nukepayload2&apos;s fork of Visual Basic Interactive Compiler [Version {0}] 的本地化字符串。
        '''</summary>
        Friend Shared ReadOnly Property LogoLine1() As String
            Get
                Return ResourceManager.GetString("LogoLine1", resourceCulture)
            End Get
        End Property
        
        '''<summary>
        '''  查找类似 Based on Roslyn [Version {0}].  的本地化字符串。
        '''</summary>
        Friend Shared ReadOnly Property LogoLine2() As String
            Get
                Return ResourceManager.GetString("LogoLine2", resourceCulture)
            End Get
        End Property
        
        '''<summary>
        '''  查找类似 This is a pre-release software. For known issues, see https://github.com/Nukepayload2/VBScriptDotNet#known-issues 的本地化字符串。
        '''</summary>
        Friend Shared ReadOnly Property LogoLine3() As String
            Get
                Return ResourceManager.GetString("LogoLine3", resourceCulture)
            End Get
        End Property
    End Class
End Namespace
