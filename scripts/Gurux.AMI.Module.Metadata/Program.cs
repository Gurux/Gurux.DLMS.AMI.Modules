using Gurux.AMI.Module.Metadata;

if (args.Length != 3)
{
    Console.Error.WriteLine("Usage: Gurux.AMI.Module.Metadata <publish-directory> <module-id> <version>");
    return 2;
}
try
{
    Console.WriteLine(CompatibilityMetadata.Write(args[0], args[1], args[2]));
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine("Cannot create module compatibility metadata: " + error.Message);
    return 1;
}
