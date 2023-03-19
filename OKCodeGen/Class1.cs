using Orleans;
//Orleans generates C# code for serialization so need a C# project to compile the generated code

[assembly: GenerateCodeForDeclaringAssembly(typeof(OKGrains.StreamData))]