using System.Reflection;
using U = Husky.Utility;

namespace Husky;

public static class CliActions
{
   private const string HUSKY_FOLDER_NAME = ".husky";

   // Custom dir help TODO: change this url to short version of docs
   public const string DOCS_URL = "https://github.com/alirezanet/husky.net";

   public static async Task<int> Install(string? dir = null)
   {
      // Ensure that we're inside a git repository
      // If git command is not found, we should return exception.
      // That's why ExitCode needs to be checked explicitly.
      if ((await Git.ExecAsync("rev-parse")).ExitCode != 0)
      {
         "Not a git repository".LogErr();
         return 1;
      }

      var cwd = Environment.CurrentDirectory;

      // set default husky folder
      dir ??= HUSKY_FOLDER_NAME;
      var path = Path.GetFullPath(Path.Combine(cwd, dir));

      // Ensure that we're not trying to install outside of cwd
      if (!path.StartsWith(cwd))
      {
         $"{cwd}\nnot allowed (see {DOCS_URL})".LogErr();
         return 1;
      }

      // Ensure that cwd is git top level
      if (!Directory.Exists(Path.Combine(cwd, ".git")))
      {
         $".git can't be found (see {DOCS_URL})".LogErr();
         return 1;
      }

      try
      {
         // Create .husky/_
         Directory.CreateDirectory(Path.Combine(path, "_"));

         // Create .husky/_/.  ignore
         await File.WriteAllTextAsync(Path.Combine(path, "_/.gitignore"), "*");

         // Copy husky.sh to .husky/_/husky.sh
         var husky_shPath = Path.Combine(path, "_/husky.sh");
         {
            await using var stream = Assembly.GetAssembly(typeof(Program))!.GetManifestResourceStream("Husky.templates.husky.sh")!;
            using var sr = new StreamReader(stream);
            var content = await sr.ReadToEndAsync();
            await File.WriteAllTextAsync(husky_shPath, content);
         }

         // find all hooks (if exists) from .husky/ and add executable flag (except json files)
         var files = Directory.GetFiles(path).Where(f => !f.EndsWith(".json")).ToList();
         files.Add(husky_shPath);
         await Utility.SetExecutablePermission(files.ToArray());

         // Created task-runner.json file
         // We don't want to override this file
         if (!File.Exists(Path.Combine(path, "task-runner.json")))
         {
            await using var stream = Assembly.GetAssembly(typeof(Program))!.GetManifestResourceStream("Husky.templates.task-runner.json")!;
            using var sr = new StreamReader(stream);
            var content = await sr.ReadToEndAsync();
            await File.WriteAllTextAsync(Path.Combine(path, "task-runner.json"), content);
         }

         // Configure repo
         var p = await Git.ExecAsync($"config core.hooksPath {dir}");
         if (p.ExitCode != 0)
         {
            "Failed to configure git".LogErr();
            return 1;
         }
      }
      catch (Exception e)
      {
         e.Message.LogVerbose(ConsoleColor.DarkRed);
         "Git hooks installation failed".LogErr();
         return 1;
      }

      "Git hooks installed".Log(ConsoleColor.Green);
      return 0;
   }

   public static int Version()
   {
      var v = Assembly.GetAssembly(typeof(CliActions))?.GetName().Version?.ToString() ?? throw new Exception("Something is not right!");
      v.Log();
      return 0;
   }

   public static async Task<int> Uninstall()
   {
      var p = await Git.ExecAsync("config --unset core.hooksPath");
      if (p.ExitCode != 0)
      {
         "Failed to uninstall git hooks".LogErr();
         return 1;
      }

      "Git hooks successfully uninstalled".Log(ConsoleColor.Green);
      return 0;
   }


   public static async Task<int> Set(string file, string cmd)
   {
      var dir = Path.GetDirectoryName(file);
      if (!Directory.Exists(dir))
      {
         $"can't create hook, {dir} directory doesn't exist (try running husky install)".LogErr();
         return 1;
      }

      {
         await using var stream = Assembly.GetAssembly(typeof(Program))!.GetManifestResourceStream("Husky.templates.hook")!;
         using var sr = new StreamReader(stream);
         var content = await sr.ReadToEndAsync();
         await File.WriteAllTextAsync(file, $"{content}\n{cmd}");
      }

      // needed for linux
      await Utility.SetExecutablePermission(file);

      $"created {file}".Log(ConsoleColor.Green);
      return 0;
   }

   public static async Task<int> Add(string file, string cmd)
   {
      if (!File.Exists(file)) return await Set(file, cmd);

      await File.AppendAllTextAsync(file, $"{cmd}\n");
      "added to hook".Log();
      return 0;
   }

   public static async Task<int> Run(string[]? args = default)
   {
      if (args is null || args.Length == 0)
         return await TaskRunner.Run();

      var dic = Utility.ParseArgs(args);

      // ReSharper disable once InvertIf
      if (dic.Keys.Any(q => q != "name" && q != "group"))
      {
         "invalid arguments.".LogErr();
         return 1;
      }

      return await TaskRunner.Run(dic);
   }
}
