# Code Sample Guidelines

## GitHub Page

To ensure consistency and discoverability of our code samples on  
https://github.com/autodesk-platform-services,  
please follow the conventions explained below.

Also, note that the official **Code Samples** gallery is automatically generated from these GitHub repositories, and it relies on some of these conventions.

---

### About Section (Top-Left Corner)

Update the **About** section in the top-left corner with the following information:

- **Description** – short description of the code sample (used by the gallery)
- **Website** – where applicable, include a link to live demo (used by the gallery)
- **Topics** – provide topics relevant to the code sample

---

### Topics

#### Type of repository

- `sample`
- `sdk`

> Only repos with the `sample` topic are included in the gallery.

---

#### Programming language

- `nodejs`
- `csharp`
- `java`
- `go`
- `php`
- `python`

> Used for filtering in the gallery.

---

#### APS services and components

- `autodesk-data-management`
- `autodesk-model-derivative`
- `autodesk-reality-capture`
- `autodesk-design-automation` (formerly `autodesk-designautomation`)
- `autodesk-webhook`
- `autodesk-bim360`
- `autodesk-construction-cloud`
- `autodesk-data-exchange`
- `autodesk-aec-data-model`
- `autodesk-mfg-data-model`
- `autodesk-viewer`

> Used for filtering in the gallery.

---

#### Design Automation engines

- `autodesk-autocad`
- `autodesk-revit`
- `autodesk-inventor`
- `autodesk-3dsmax`

---

#### Other technologies

- `express`
- `aspnet`
- `react`
- `graphql`
- etc.

> Follow suggestions provided by GitHub.

---

## README

Make sure the `README.md` file contains the following:

### 1. Level-1 header with a title  
(Required by the gallery)

Example:
# My Sample App

### 2. Badges for supported platforms, runtime versions, and license

Example badge markup:

![platforms](https://img.shields.io/badge/platform-windows%20%7C%20osx%20%7C%20linux-lightgray.svg)
[![.net](https://img.shields.io/badge/net-6.0-blue.svg)](https://dotnet.microsoft.com/en-us/download/dotnet/6.0)
[![node.js](https://img.shields.io/badge/Node.js-20.13-blue.svg)](https://nodejs.org)
[![npm](https://img.shields.io/badge/npm-10.5-blue.svg)](https://www.npmjs.com/)
[![license](https://img.shields.io/:license-mit-green.svg)](https://opensource.org/licenses/MIT)


---

### 3. Description of the code sample

More detailed than the short description in the **About** section.

---

### 4. Thumbnail (Required by the gallery)

- Must be stored in the root folder of the repo
- Named either:
  - `thumbnail.png`
  - `thumbnail.gif`
- Aspect ratio: 16:9
- Ideally no more than 200kB

---

## Usage

If there is a live demo, include description of how it can be used, or a demonstration video.

---

## Development

Include:

### Prerequisites

For example:

- APS app credentials
- Provisioned access to ACC
- Node.js or .NET runtime
- bash terminal

---

### Steps to build and run the sample locally

---

Where applicable, include additional information such as:

- Deployment steps
- Known limitations
- Troubleshooting
- Tips & tricks
- Additional links to documentation and/or support

---

## License

All samples must use the **MIT license**.

Include a `LICENSE` file in the repository as well.
