# Podcast Project

This project is a podcast management and feed generation application. It consists of a C# backend for handling podcast data and feed generation, and a simple web frontend for displaying podcast information.

## Project Structure

- **src/**: Contains the C# backend source code.
  - `DrPodcast.csproj`, `DrPodcast.sln`: Project and solution files.
  - `PodcastFeedGenerator.cs`: Logic for generating podcast feeds.
  - `PodcastModels.cs`: Data models for podcasts.

- **site/**: Contains the web frontend.
  - `index.html`: Main web page.
  - `script.js`: Frontend JavaScript logic.
  - `styles.css`: Styles for the frontend.

- **podcasts.json**: Stores podcast metadata.

## Usage

1. **Backend**:  
   Build and run the C# backend using .NET CLI:
   ```bash
   dotnet build src/DrPodcast.csproj
   dotnet run --project src/DrPodcast.csproj
   ```

2. **Frontend**:  
   Open `site/index.html` in your browser to view the podcast site.

## Requirements

- [.NET SDK](https://dotnet.microsoft.com/download) (for backend)
- Modern web browser (for frontend)

## License

This project is provided as-is for educational purposes.