using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
var root=Path.GetFullPath(args[0]);
var artifactDir=Path.Combine(root,"ControlPlus.Api/bin/Debug/net10.0");
System.Runtime.Loader.AssemblyLoadContext.Default.Resolving += (context,name) => {
 var path=Path.Combine(artifactDir,name.Name+".dll");
 return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
};
var windowsRoot="C:/ControlPlus/backend/ControlPlus.Backend/";
var reports=new List<object>();
foreach(var name in new[]{"ControlPlus.Api","ControlPlus.Application","ControlPlus.Infrastructure","ControlPlus.Domain"})
{
 var file=Path.Combine(root,"ControlPlus.Api/bin/Debug/net10.0",name+".dll");
 var testFile=Path.Combine(root,"ControlPlus.Api.Tests/bin/Debug/net10.0",name+".dll");
 var pdbFile=Path.ChangeExtension(file,".pdb");
 using var pe=new PEReader(File.OpenRead(file));var mr=pe.GetMetadataReader();
 using var provider=MetadataReaderProvider.FromPortablePdbStream(File.OpenRead(pdbFile));var pdb=provider.GetMetadataReader();
 var cv=pe.ReadDebugDirectory().Single(x=>x.Type==DebugDirectoryEntryType.CodeView);
 var cvd=pe.ReadCodeViewDebugDirectoryData(cv);var pdbId=pdb.DebugMetadataHeader!.Id.ToArray();
 var documents=new List<object>();int matched=0,diverged=0,generated=0,external=0;
 foreach(var handle in pdb.Documents)
 {
  var doc=pdb.GetDocument(handle);var source=pdb.GetString(doc.Name).Replace('\\','/');
  var expected=Convert.ToHexString(pdb.GetBlobBytes(doc.Hash));var alg=pdb.GetGuid(doc.HashAlgorithm);
  bool isMapped=source.StartsWith(windowsRoot,StringComparison.OrdinalIgnoreCase);
  if(!isMapped){external++;continue;}
  var relative=source[windowsRoot.Length..];var local=Path.Combine(root,relative);
  bool isGenerated=relative.Contains("/obj/",StringComparison.OrdinalIgnoreCase);
  if(isGenerated){generated++;continue;}
  var exists=File.Exists(local);
  string? actual=exists ? Convert.ToHexString(alg==new Guid("8829d00f-11b8-4213-878b-770e8597ac16")?SHA256.HashData(File.ReadAllBytes(local)):SHA1.HashData(File.ReadAllBytes(local))):null;
  var same=expected==actual;if(same)matched++;else diverged++;
  documents.Add(new{path=relative,algorithm=alg==new Guid("8829d00f-11b8-4213-878b-770e8597ac16")?"SHA256":"SHA1",expected,actual,match=same});
 }
 var sha=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
 reports.Add(new{name,sha256=sha,testCopySame=File.Exists(testFile)&&sha==Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(testFile))),
 pdbBound=cvd.Guid==new Guid(pdbId[..16])&&cv.Stamp==BitConverter.ToUInt32(pdbId,16),
 pdbSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(pdbFile))),
 mvid=mr.GetGuid(mr.GetModuleDefinition().Mvid),lastWriteUtc=File.GetLastWriteTimeUtc(file),matched,diverged,generated,external,documents});
}
Console.WriteLine(JsonSerializer.Serialize(new { assemblies=reports, runtime=ArtifactRuntimeInspection.Run() },new JsonSerializerOptions{WriteIndented=true}));

internal static class ArtifactRuntimeInspection
{
 public static object Run()
 {
  using var db=new ControlPlus.Infrastructure.Persistence.ControlPlusDbContext(
   Microsoft.EntityFrameworkCore.NpgsqlDbContextOptionsBuilderExtensions.UseNpgsql(
    new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<ControlPlus.Infrastructure.Persistence.ControlPlusDbContext>(),
    "Host=127.0.0.1;Port=1;Database=offline_catalog_inspection;Username=offline_no_connection").Options);
  var migrations=Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.GetMigrations(db.Database).ToArray();
  var migrator=Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>(db.Database);
  var sql=migrator.GenerateScript("20261028010000_SaleReceiptDataSnapshotV1","20261029010000_PermanentProductReferencesV1");
  var parsed=ControlPlus.Api.Migrations.ControlledExecutionCommand.Parse(
   new[]{"--preflight-to","20261029010000_PermanentProductReferencesV1"},"true");
  var unsupported=ControlPlus.Api.Migrations.ControlledExecutionCommand.Parse(
   new[]{"--postflight-to","20261029010000_PermanentProductReferencesV1"},"true");
  return new { migrations, predecessorCount=Array.IndexOf(migrations,"20261029010000_PermanentProductReferencesV1"),
   preflightAccepted=parsed.IsValid&&parsed.Mode==ControlPlus.Api.Migrations.ControlledExecutionMode.Preflight,
   postflightRejected=!unsupported.IsValid, sqlSha256=Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(sql))),
   containsRegistry=sql.Contains("CREATE TABLE catalogo.producto_codigo"),
   humanizerLoaded=AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name=="Humanizer"),
   efDesignLoaded=AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name=="Microsoft.EntityFrameworkCore.Design"),
   connectionOpened=Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.GetDbConnection(db.Database).State!=System.Data.ConnectionState.Closed };
 }
}
