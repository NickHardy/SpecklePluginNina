namespace NINA.Plugin.Speckle.Dockables {

    public class LoadedListVM {

        public LoadedListVM(string path, int targetCount) {
            Path = path;
            Name = System.IO.Path.GetFileNameWithoutExtension(path);
            TargetCount = targetCount;
        }

        public string Path { get; }

        public string Name { get; }

        public int TargetCount { get; }

        public string Tooltip => Path + " - " + TargetCount + (TargetCount == 1 ? " target" : " targets");
    }
}
