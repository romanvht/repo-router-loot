using RouterLoot.Assets;

try
{
    if (args.Length != 2)
    {
        throw new ArgumentException("Usage: AssetTool <source-assets-directory> <generated-output-directory>");
    }

    ImportPipeline.Prepare(args[0], args[1]);

    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine("Asset import failed: " + error.Message);

    return 1;
}
