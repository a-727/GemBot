using Newtonsoft.Json;

namespace GemBot;

public class Tutorial
{
    public List<Step> Steps { get; set; }
    public string Name { get; set; }
    public static Tutorial Load(string dat)
    {
        Tutorial tutorial = new Tutorial
        {
            Steps = []
        };
        string[] baseSteps = dat.Split("\n#");
        tutorial.Name = baseSteps[0];
        string[] stepsStr = baseSteps[1..];
        foreach (string stepStr in stepsStr)
        {
            Step step = new Step();
            string[] stepSettings = stepStr.Split("\n");
            step.Name = stepSettings[0];
            foreach (string line in stepSettings[1..])
            {
                string[] option = line.Split(": ");
                switch (option[0])
                {
                    case " > Requirement" or "Requirement":
                        step.Requirements = option[1].Split(" ");
                        break;
                    case " > Description" or "Description":
                        step.Description = string.Join(' ', option[1..]);
                        break;
                    case " > Flexible" or "Flexible":
                        if (option[1] == "True" || option[1] == "true" || option[1] == "yes")
                        {
                            step.Flexible = true;
                        }
                        break;
                }
            }
            tutorial.Steps.Add(step);
        }
        return tutorial;
    }
    public static async Task<List<Tutorial>> LoadAll(ushort max)
    {
        List<Tutorial> toReturn = [];
        string[] dat = JsonConvert.DeserializeObject<string[]>(await File.ReadAllTextAsync("Tutorial/map.info")) ?? throw new InvalidOperationException();
        
        for (ushort i = 0; i < max; i++)
        {
            toReturn.Add(Load(await File.ReadAllTextAsync($"Tutorial/{dat[i]}")));
        }
        return toReturn;
    }
    public override string ToString()
    {
        string text =  $"Tutorial {Name} Steps: [";
        foreach (Step step in Steps)
        {
            text += "(" + step + "), ";
        }
        text = text.TrimEnd(',', ' ');
        text += "]";
        return text;
    }
}
public class Step (string name = "Example Tutorial", string[]? requirements = null, string description = "Do something", bool flexible = false)
{
    public string Name { get; set; } = name;
    public string Description { get; set; } = description;
    public string[] Requirements { get; set; } = requirements ?? ["command"];
    public bool Flexible = flexible;
    public override string ToString()
    {
        string text =  $"\"Name\": {Name}, \"Description\": {Description}, \"Requirements\": [";
        foreach (string requirement in Requirements)
        {
            text += "(\"" + requirement + "\"), ";
        }
        text = text.TrimEnd(',', ' ');
        text += "]";
        return text;
    }
}