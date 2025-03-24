# ECS 235A Course Project - Fall 2024

This project is a part of the ECS 235A course for Fall 2024. The goal of this project is to contribute to [OSS-Gadget](https://github.com/microsoft/OSSGadget.git), an open-source tool developed by Microsoft that provides security and analysis utilities for open-source software.

## Group Members

| Name     | Email                 |
|---------|-----------------------|
| Humaira Fasih Ahmed Hashmi  | <hfhashmi@ucdavis.edu> |
| Sarah Yuniar | <sayuniar@ucdavis.edu> |
| Parnian  | <pkamran@ucdavis.edu>   |

# Table of Contents

1. [Installation](#installation)
2. [New Contrubutions](#new-contributions)
3. [Analysis](#metadata-analysis)

### New Contributions:

- Added [data](https://github.com/Parniaan/oss-gadget/tree/main/src/data) directory that includes pypi and npm popular and unpopular packages and the json file extracted from Typomind as the tokenization data
- Modified [FindSquatsTool.cs](https://github.com/Parniaan/oss-gadget/blob/main/src/oss-find-squats/FindSquatsTool.cs) to accept our compiled lists for pypi and npm
- Added [PackageTokenMutator.cs](src/oss-find-squats-lib/Mutators/PackageTokenMutator.cs) as a new mutator for detecting prefix/suffix augmentations based on the given algorithm and rule in Typomind paper.
- To use the tool based on our added features these modifications should be applied to the code:
  - In [FindSquatsTool.cs](https://github.com/Parniaan/oss-gadget/blob/main/src/oss-find-squats/FindSquatsTool.cs) the absoulte path to pypi or npm packages should be added to the code - check placeholder instead of the following placeholders in the code:
```
   List<string> allPackages = new List<string>();
            List<string> csvs = new List<string> { 
                //Should be replace with absolute path to pypi_popular.csv and pypi_unpopular.csv files 
                // or npm_popular.csv and npm_unpopular.csv files in the data/npm directory or data/pypi directory
                // @"<path-to-pypi-unpopular-csv-file>"
                // @"<path-to-pypi-popular-csv-file>"
            };

```
  - In [PackageTokenMutator.cs](src/oss-find-squats-lib/Mutators/PackageTokenMutator.cs) the absolute path to the [token_data.json](src/data/toeknization/token_data.json) should be added to the following line

```
    private string JsonFilePath = "absolute_path_to_token_data";
```

