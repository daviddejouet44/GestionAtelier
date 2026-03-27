import { NavLink } from "react-router-dom";
import {
  LayoutDashboard,
  Users,
  Printer,
  FileText,
  Package,
  BarChart3,
} from "lucide-react";

const navItems = [
  { to: "/", icon: LayoutDashboard, label: "Tableau de bord" },
  { to: "/clients", icon: Users, label: "Clients" },
  { to: "/machines", icon: Printer, label: "Machines" },
  { to: "/travaux", icon: FileText, label: "Travaux" },
  { to: "/inventaire", icon: Package, label: "Inventaire" },
  { to: "/rapports", icon: BarChart3, label: "Rapports" },
];

export default function Sidebar() {
  return (
    <aside className="sidebar">
      <div className="sidebar-logo">
        <Printer size={28} />
        <span>GestionAtelier</span>
      </div>
      <nav className="sidebar-nav">
        {navItems.map((item) => {
          const Icon = item.icon;
          return (
            <NavLink
              key={item.to}
              to={item.to}
              end={item.to === "/"}
              className={({ isActive }) =>
                `sidebar-link${isActive ? " active" : ""}`
              }
            >
              <Icon size={20} />
              <span>{item.label}</span>
            </NavLink>
          );
        })}
      </nav>
      <div className="sidebar-footer">
        <span>v1.0.0</span>
      </div>
    </aside>
  );
}
