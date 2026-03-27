import { BrowserRouter, Routes, Route } from "react-router-dom";
import { AppProvider } from "./context/AppContext";
import Sidebar from "./components/Sidebar";
import Dashboard from "./pages/Dashboard";
import Clients from "./pages/Clients";
import Machines from "./pages/Machines";
import Travaux from "./pages/Travaux";
import Inventaire from "./pages/Inventaire";
import Rapports from "./pages/Rapports";
import "./App.css";

function App() {
  return (
    <AppProvider>
      <BrowserRouter>
        <div className="app-layout">
          <Sidebar />
          <main className="main-content">
            <Routes>
              <Route path="/" element={<Dashboard />} />
              <Route path="/clients" element={<Clients />} />
              <Route path="/machines" element={<Machines />} />
              <Route path="/travaux" element={<Travaux />} />
              <Route path="/inventaire" element={<Inventaire />} />
              <Route path="/rapports" element={<Rapports />} />
            </Routes>
          </main>
        </div>
      </BrowserRouter>
    </AppProvider>
  );
}

export default App;
