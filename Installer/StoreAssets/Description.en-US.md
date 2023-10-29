This app is Nukepayload2's fork of Visual Basic Interactive Compiler.
It enables the use of Visual Basic .NET as a scripting language by executing source code snippets directly.
You'll be able to access .NET 6, Windows Forms and WPF APIs by writing VB code without creating VB projects. 
It can also run *.vbx files, which is a modern .NET replacement of *.vbs (VBScript) files. Before running script files by file association, you'll need to confirm the security warning. 
Please note that this is a pre-release software. Before reporting bugs, please check known issues from the project URL.
Technical information of the scripting runtime:
- Visual Basic version: 16.9
- Visual Basic compiler: Roslyn 4.7.0 with some patches for running scripts
- Embedded .NET runtime: .NETCore.App 6.0.22 and WindowsDesktop.App 6.0.22
- Target framework of the script host: net6.0-windows10.0.17763.0