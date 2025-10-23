import { Link } from "react-router-dom";

function NotFound() {
  return (
    <div className="page" aria-labelledby="not-found-title">
      <section className="page-section">
        <h2 id="not-found-title">Page not found</h2>
        <p>The page you were looking for does not exist.</p>
        <Link to="/" className="primary" style={{ display: "inline-flex", marginTop: "1rem" }}>
          Back to overview
        </Link>
      </section>
    </div>
  );
}

export default NotFound;
