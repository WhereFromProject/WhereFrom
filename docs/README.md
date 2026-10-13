# WhereFrom documentation

Start with the [English README](../README.md) or [中文说明](../README.zh-CN.md) for installation and commands.

| Document | Purpose |
| --- | --- |
| [Engineering design](../ENGINEERING.md) | Implemented architecture, evidence rules, and maintenance boundaries |
| [Browser setup](browser-setup.txt) | Load the Chrome/Edge extension and connect the Native Host |
| [Release validation](release-validation.md) | Repeatable checks for the ZIP, installer, and clean Windows environment |

The previous v0.1.1 release is CLI-only. The v0.2.0 release adds browser capture, a GUI, and an Explorer file menu. Stage journals, personal test files, and browser profiles are kept outside the delivery tree. The [Windows workflow](../.github/workflows/windows.yml) defines automated checks for pull requests and pushes.
