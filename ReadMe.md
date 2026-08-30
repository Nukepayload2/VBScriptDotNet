# VBScriptDotNet
A patched VB interactive that runs with stable releases of Roslyn.

## How to run VB interactive
### Install binaries
<a href="ms-windows-store://pdp/?ProductId=9N210C9TDZ95&mode=mini">
   <img src="https://get.microsoft.com/images/en-us%20dark.svg" alt="Download VB Interactive" />
</a>

Store page: https://www.microsoft.com/store/productId/9N210C9TDZ95?ocid=pdpshare

## Branches
- main: The baseline. Roslyn is unmodified. This branch is not actively maintained.
- [minimum-modified-roslyn](https://github.com/Nukepayload2/VBScriptDotNet/tree/minimum-modified-roslyn) Roslyn has been slightly modified to enable basic `.vbx` scripting features.
- [use-modified-roslyn](https://github.com/Nukepayload2/VBScriptDotNet/tree/use-modified-roslyn) Roslyn has been modified to fix critical bugs. You're able to use `Await`, `AddHandler` and `RemoveHandler` in this branch.
- [with-modified-vbsyntax](https://github.com/Nukepayload2/VBScriptDotNet/tree/with-modified-vbsyntax) Roslyn has been heavily modified. The `.vbx` script is able to interop with high performance APIs, such as `ref struct` and `allows ref struct`.
